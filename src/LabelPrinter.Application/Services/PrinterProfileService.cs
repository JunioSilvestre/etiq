using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Entities;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelPrinter.Application.Services;

/// <summary>
/// Gerencia perfis de impressora.
/// Sincroniza configuração do appsettings.json com o banco de dados.
/// Resolve nome de impressora considerando diferenças entre máquinas.
/// </summary>
public class PrinterProfileService : IPrinterProfileService
{
    private readonly ILogger<PrinterProfileService> _logger;
    private readonly IPrinterProfileRepository _profileRepository;
    private readonly IPrinterDiscoveryService _discoveryService;
    private readonly IPrinterHealthService _healthService;
    private readonly PrintingOptions _printingOptions;

    public PrinterProfileService(
        ILogger<PrinterProfileService> logger,
        IPrinterProfileRepository profileRepository,
        IPrinterDiscoveryService discoveryService,
        IPrinterHealthService healthService,
        IOptions<PrintingOptions> printingOptions)
    {
        _logger = logger;
        _profileRepository = profileRepository;
        _discoveryService = discoveryService;
        _healthService = healthService;
        _printingOptions = printingOptions.Value;
    }

    public async Task<IReadOnlyList<PrinterProfile>> GetEnabledProfilesAsync(
        CancellationToken cancellationToken = default) =>
        await _profileRepository.GetEnabledAsync(cancellationToken);

    public async Task<PrinterProfile?> GetProfileByNameAsync(
        string windowsPrinterName,
        CancellationToken cancellationToken = default)
    {
        var all = await _profileRepository.GetAllAsync(cancellationToken);
        return all.FirstOrDefault(p =>
            p.WindowsPrinterName.Equals(windowsPrinterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sincroniza os perfis configurados no appsettings.json com o banco de dados.
    /// Tenta resolver o nome exato da impressora via WMI — tratando diferenças entre máquinas.
    /// </summary>
    public async Task SyncProfilesFromConfigAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sincronizando perfis de impressora com o banco de dados...");

        var discoveredPrinters = await _discoveryService.DiscoverPrintersAsync(cancellationToken);

        _logger.LogInformation("Impressoras descobertas via WMI:");
        foreach (var printer in discoveredPrinters)
        {
            _logger.LogInformation("  → {Name} | Driver: {Driver} | Porta: {Port} | Online: {Online}",
                printer.Name, printer.DriverName, printer.PortName, printer.IsOnline);
        }

        foreach (var config in _printingOptions.Printers)
        {
            if (!config.Enabled) continue;

            var resolvedPrinterName = ResolveWindowsPrinterName(config, discoveredPrinters);

            if (resolvedPrinterName == null)
            {
                _logger.LogWarning(
                    "Impressora '{Name}' (configurada como '{WindowsName}') não encontrada no sistema. " +
                    "O perfil será salvo mas desativado até que a impressora seja encontrada.",
                    config.Name, config.WindowsPrinterName);
                continue;
            }

            _logger.LogInformation(
                "Perfil '{Name}' → impressora Windows: '{WindowsName}'",
                config.Name, resolvedPrinterName);

            var protocol = Enum.TryParse<PrinterProtocol>(config.Protocol, true, out var p)
                ? p : PrinterProtocol.GhostScript;

            var profile = new PrinterProfile
            {
                Id = DeterministicGuid(config.Name), // ID estável para evitar duplicatas
                Name = config.Name,
                Description = config.Description,
                WindowsPrinterName = resolvedPrinterName,
                Protocol = protocol,
                Dpi = config.Dpi,
                DefaultLabelWidthMm = config.DefaultLabelWidthMm,
                DefaultLabelHeightMm = config.DefaultLabelHeightMm,
                Enabled = true,
                Priority = config.Priority,
                MaxRetries = config.MaxRetries,
                RetryDelaySeconds = config.RetryDelaySeconds,
                DefaultCopies = config.DefaultCopies
            };

            // Tentar obter driver name da impressora descoberta
            var discovered = discoveredPrinters.FirstOrDefault(d =>
                d.Name.Equals(resolvedPrinterName, StringComparison.OrdinalIgnoreCase));
            if (discovered != null)
            {
                profile.DriverName = discovered.DriverName;
                profile.PortName = discovered.PortName;
            }

            await _profileRepository.AddOrUpdateAsync(profile, cancellationToken);
        }

        _logger.LogInformation("Sincronização de perfis concluída.");
    }

    /// <summary>
    /// Seleciona a impressora mais adequada para um job específico.
    /// Aplica regras de roteamento: marketplace, tamanho de etiqueta, prioridade.
    /// </summary>
    public async Task<PrinterProfile?> SelectPrinterForJobAsync(
        PrintJob job,
        CancellationToken cancellationToken = default)
    {
        var profiles = await _profileRepository.GetEnabledAsync(cancellationToken);

        if (!profiles.Any())
        {
            _logger.LogWarning("Nenhum perfil de impressora habilitado.");
            return null;
        }

        // Tentar regras de roteamento específicas
        foreach (var profile in profiles.OrderBy(p => p.Priority))
        {
            foreach (var rule in profile.RoutingRules.Where(r => r.Enabled).OrderBy(r => r.Priority))
            {
                var marketplaceMatch = rule.Marketplace == null || rule.Marketplace == job.Marketplace;
                var sizeMatch = rule.LabelSize == null || rule.LabelSize == job.LabelSize;

                if (marketplaceMatch && sizeMatch)
                {
                    var isOnline = await _healthService.IsPrinterOnlineAsync(
                        profile.WindowsPrinterName, cancellationToken);

                    if (isOnline)
                    {
                        _logger.LogInformation(
                            "[Job {JobId}] Impressora selecionada por regra de roteamento: {Printer} (Marketplace: {Marketplace}, Tamanho: {Size})",
                            job.Id, profile.Name, rule.Marketplace?.ToString() ?? "any", rule.LabelSize?.ToString() ?? "any");
                        return profile;
                    }
                }
            }
        }

        // Fallback: primeira impressora online por prioridade
        foreach (var profile in profiles.OrderBy(p => p.Priority))
        {
            var isOnline = await _healthService.IsPrinterOnlineAsync(
                profile.WindowsPrinterName, cancellationToken);

            if (isOnline)
            {
                _logger.LogInformation(
                    "[Job {JobId}] Impressora selecionada por fallback (prioridade): {Printer}",
                    job.Id, profile.Name);
                return profile;
            }
        }

        _logger.LogWarning("[Job {JobId}] Nenhuma impressora online disponível.", job.Id);
        return null;
    }

    /// <summary>
    /// Resolve o nome exato da impressora no Windows.
    /// Trata diferenças entre máquinas: tenta nome exato, depois padrão de driver, depois qualquer disponível.
    /// </summary>
    private string? ResolveWindowsPrinterName(
        PrinterProfileConfig config,
        IReadOnlyList<PrinterInfo> discovered)
    {
        // 1. Nome exato configurado
        if (!string.IsNullOrWhiteSpace(config.WindowsPrinterName))
        {
            var exact = discovered.FirstOrDefault(p =>
                p.Name.Equals(config.WindowsPrinterName, StringComparison.OrdinalIgnoreCase) &&
                !p.IsDuplicate);
            if (exact != null) return exact.Name;

            _logger.LogWarning(
                "Impressora '{ConfiguredName}' não encontrada. Tentando matching por driver...",
                config.WindowsPrinterName);
        }

        // 2. Pattern matching por nome do driver ou nome da impressora (múltiplas palavras-chave)
        if (!string.IsNullOrWhiteSpace(config.DriverNamePattern))
        {
            var patterns = config.DriverNamePattern.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var match = discovered.FirstOrDefault(p =>
                !p.IsDuplicate &&
                patterns.Any(pattern =>
                    p.DriverName?.Contains(pattern.Trim(), StringComparison.OrdinalIgnoreCase) == true ||
                    p.Name.Contains(pattern.Trim(), StringComparison.OrdinalIgnoreCase)
                ));

            if (match != null)
            {
                _logger.LogInformation(
                    "Impressora resolvida por pattern '{Pattern}': '{Name}'",
                    config.DriverNamePattern, match.Name);
                return match.Name;
            }
        }

        // 3. Fallback: tentar nome parcial
        if (!string.IsNullOrWhiteSpace(config.WindowsPrinterName))
        {
            var partial = discovered.FirstOrDefault(p =>
                p.Name.Contains(config.WindowsPrinterName, StringComparison.OrdinalIgnoreCase) &&
                !p.IsDuplicate);
            if (partial != null)
            {
                _logger.LogInformation(
                    "Impressora resolvida por correspondência parcial: '{Name}'", partial.Name);
                return partial.Name;
            }
        }

        return null;
    }

    /// <summary>Gera um GUID determinístico baseado no nome do perfil (para evitar duplicatas no banco).</summary>
    private static Guid DeterministicGuid(string name)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"profile:{name}"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50); // UUID version 5
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80); // UUID variant
        return new Guid(bytes);
    }
}
