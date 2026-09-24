using System.Management;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;

namespace LabelPrinter.Infrastructure.Printers;

/// <summary>
/// Descobre impressoras instaladas via WMI (Win32_Printer).
/// Nunca hardcoda nomes — consulta o sistema diretamente.
/// Detecta e marca cópias duplicadas na mesma porta.
/// </summary>
public class PrinterDiscoveryService : IPrinterDiscoveryService
{
    private readonly ILogger<PrinterDiscoveryService> _logger;

    // Drivers de impressoras virtuais que devem ser excluídos por padrão
    private static readonly HashSet<string> VirtualDriverPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Print To PDF",
        "Microsoft XPS Document Writer",
        "Send To OneNote",
        "Fax",
        "PDF Creator",
        "PDF24"
    };

    public PrinterDiscoveryService(ILogger<PrinterDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<PrinterInfo>> DiscoverPrintersAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => QueryPrinters(), cancellationToken);
    }

    public async Task<PrinterInfo?> GetPrinterInfoAsync(
        string printerName,
        CancellationToken cancellationToken = default)
    {
        var all = await DiscoverPrintersAsync(cancellationToken);
        return all.FirstOrDefault(p => p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<PrinterInfo>> GetThermalPrinterCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await DiscoverPrintersAsync(cancellationToken);

        // Filtra impressoras virtuais
        var physical = all.Where(p => !IsVirtualPrinter(p)).ToList();

        // Detecta e marca cópias duplicadas (mesma porta)
        var portGroups = physical
            .Where(p => !string.IsNullOrWhiteSpace(p.PortName))
            .GroupBy(p => p.PortName!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in portGroups)
        {
            var sorted = group.OrderBy(p => p.Name).ToList();
            _logger.LogWarning(
                "Impressoras duplicadas detectadas na porta {Port}: {Names}. Apenas {Primary} será usada por padrão.",
                group.Key,
                string.Join(", ", sorted.Select(p => p.Name)),
                sorted.First().Name);

            // Marca as cópias (não a primeira)
            foreach (var duplicate in sorted.Skip(1))
            {
                duplicate.IsDuplicate = true;
            }
        }

        return physical;
    }

    private IReadOnlyList<PrinterInfo> QueryPrinters()
    {
        var printers = new List<PrinterInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"SELECT Name, DriverName, PortName, PrinterStatus, Default, JobCountSinceLastReset,
                          PrintProcessor, HorizontalResolution, VerticalResolution,
                          WorkOffline, Queued, PrintJobDataType, Comment
                  FROM Win32_Printer");

            foreach (ManagementObject printer in searcher.Get())
            {
                try
                {
                    var info = ExtractPrinterInfo(printer);
                    printers.Add(info);
                    _logger.LogDebug(
                        "Impressora encontrada: {Name} | Driver: {Driver} | Porta: {Port} | Online: {Online} | Padrão: {Default}",
                        info.Name, info.DriverName, info.PortName, info.IsOnline, info.IsDefault);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erro ao extrair informações de uma impressora via WMI.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar impressoras via WMI (Win32_Printer).");
        }

        _logger.LogInformation("Descoberta de impressoras concluída: {Count} impressoras encontradas.", printers.Count);
        return printers;
    }

    private static PrinterInfo ExtractPrinterInfo(ManagementObject printer)
    {
        var name = printer["Name"]?.ToString() ?? string.Empty;
        var driverName = printer["DriverName"]?.ToString();
        var portName = printer["PortName"]?.ToString();
        var isDefault = printer["Default"] is bool d && d;
        var workOffline = printer["WorkOffline"] is bool o && o;
        var printerStatus = printer["PrinterStatus"] is uint ps ? (int?)ps : null;

        // Status: 3 = Idle (online), outros = possivelmente problemas
        var isOnline = !workOffline && printerStatus is null or 3 or 4; // 3=Idle, 4=Printing

        return new PrinterInfo
        {
            Name = name,
            DriverName = driverName,
            PortName = portName,
            IsDefault = isDefault,
            IsOnline = isOnline,
            PrinterStatus = printerStatus,
            StatusDescription = GetStatusDescription(printerStatus),
            JobCount = printer["JobCountSinceLastReset"] is uint jc ? (int)jc : 0,
            PrintProcessor = printer["PrintProcessor"]?.ToString(),
            HorizontalResolution = printer["HorizontalResolution"] is uint hr ? (int?)hr : null,
            VerticalResolution = printer["VerticalResolution"] is uint vr ? (int?)vr : null,
        };
    }

    private static bool IsVirtualPrinter(PrinterInfo printer) =>
        VirtualDriverPatterns.Any(pattern =>
            printer.DriverName?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true ||
            printer.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static string GetStatusDescription(int? status) => status switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Idle",
        4 => "Printing",
        5 => "Warming Up",
        6 => "Stopped Printing",
        7 => "Offline",
        _ => status.HasValue ? $"Status {status}" : "Unknown"
    };
}

/// <summary>
/// Verifica saúde e disponibilidade das impressoras via WMI.
/// </summary>
public class PrinterHealthService : Core.Interfaces.IPrinterHealthService
{
    private readonly ILogger<PrinterHealthService> _logger;

    public PrinterHealthService(ILogger<PrinterHealthService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsPrinterOnlineAsync(
        string printerName,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT WorkOffline, PrinterStatus FROM Win32_Printer WHERE Name = '{EscapeWmiString(printerName)}'");

                foreach (ManagementObject printer in searcher.Get())
                {
                    var workOffline = printer["WorkOffline"] is bool o && o;
                    var status = printer["PrinterStatus"] is uint ps ? (int?)ps : null;
                    return !workOffline && status is null or 3 or 4;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao verificar status da impressora {Printer}.", printerName);
                return false;
            }
        }, cancellationToken);
    }

    public async Task<int> GetJobCountAsync(
        string printerName,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT JobCountSinceLastReset FROM Win32_Printer WHERE Name = '{EscapeWmiString(printerName)}'");

                foreach (ManagementObject printer in searcher.Get())
                {
                    return printer["JobCountSinceLastReset"] is uint jc ? (int)jc : 0;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao obter contagem de jobs da impressora {Printer}.", printerName);
                return 0;
            }
        }, cancellationToken);
    }

    public async Task<string> GetStatusDescriptionAsync(
        string printerName,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT PrinterStatus FROM Win32_Printer WHERE Name = '{EscapeWmiString(printerName)}'");

                foreach (ManagementObject printer in searcher.Get())
                {
                    var status = printer["PrinterStatus"] is uint ps ? (int?)ps : null;
                    return status switch
                    {
                        3 => "Idle",
                        4 => "Printing",
                        7 => "Offline",
                        _ => $"Status {status}"
                    };
                }

                return "Not Found";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao obter status da impressora {Printer}.", printerName);
                return "Error";
            }
        }, cancellationToken);
    }

    private static string EscapeWmiString(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
