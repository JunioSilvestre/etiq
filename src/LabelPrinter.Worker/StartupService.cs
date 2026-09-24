using LabelPrinter.Application.Services;
using LabelPrinter.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabelPrinter.Worker;

/// <summary>
/// Serviço de inicialização: executa ações de startup como
/// sincronização de perfis de impressora e logging de status.
/// </summary>
public class StartupService : IHostedService
{
    private readonly ILogger<StartupService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPrinterDiscoveryService _discoveryService;

    public StartupService(
        ILogger<StartupService> logger,
        IServiceProvider serviceProvider,
        IPrinterDiscoveryService discoveryService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _discoveryService = discoveryService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== LabelPrinter iniciando ===");
        _logger.LogInformation("Versão: 1.0.0 | .NET: {Runtime}", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        _logger.LogInformation("BaseDirectory: {Dir}", AppContext.BaseDirectory);

        // Descobrir e logar impressoras
        _logger.LogInformation("Descobrindo impressoras instaladas...");
        var printers = await _discoveryService.DiscoverPrintersAsync(cancellationToken);

        _logger.LogInformation("Impressoras encontradas: {Count}", printers.Count);
        foreach (var printer in printers)
        {
            _logger.LogInformation(
                "  Impressora: '{Name}' | Driver: '{Driver}' | Porta: '{Port}' | Online: {Online} | Padrão: {Default}",
                printer.Name, printer.DriverName, printer.PortName, printer.IsOnline, printer.IsDefault);
        }

        // Sincronizar perfis de impressora
        using var scope = _serviceProvider.CreateScope();
        var printerProfileService = scope.ServiceProvider.GetRequiredService<IPrinterProfileService>();
        await printerProfileService.SyncProfilesFromConfigAsync(cancellationToken);

        _logger.LogInformation("=== LabelPrinter iniciado com sucesso ===");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== LabelPrinter encerrado ===");
        return Task.CompletedTask;
    }
}
