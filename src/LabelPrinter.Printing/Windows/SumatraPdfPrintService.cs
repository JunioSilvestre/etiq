using System.Diagnostics;
using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelPrinter.Printing.Windows;

/// <summary>
/// Envia PDF para impressão usando SumatraPDF CLI.
/// Ideal para máquinas onde GhostScript não está disponível.
/// </summary>
public class SumatraPdfPrintService : IWindowsPrintService
{
    private readonly ILogger<SumatraPdfPrintService> _logger;
    private readonly PrintingOptions _options;

    public SumatraPdfPrintService(
        ILogger<SumatraPdfPrintService> logger,
        IOptions<PrintingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<PrintResult> PrintAsync(
        string pdfPath,
        string printerName,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Enviando PDF para impressão via SumatraPDF: {Pdf} → {Printer} ({Copies} cópia(s))",
            pdfPath, printerName, copies);

        if (!File.Exists(pdfPath))
            return PrintResult.Failed(PrintAttemptResult.InvalidPdf, $"PDF não encontrado: {pdfPath}");

        var sumatraPath = _options.SumatraPdfPath;
        
        // Auto-discovery fallback se não encontrar no caminho configurado
        if (!File.Exists(sumatraPath))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            
            var fallbacks = new[]
            {
                sumatraPath,
                Path.Combine(AppContext.BaseDirectory, "SumatraPDF.exe"),
                @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
                @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
                Path.Combine(localAppData, @"SumatraPDF\SumatraPDF.exe"),
                Path.Combine(userProfile, @"AppData\Local\SumatraPDF\SumatraPDF.exe")
            };

            sumatraPath = fallbacks.FirstOrDefault(File.Exists);

            if (string.IsNullOrEmpty(sumatraPath))
            {
                _logger.LogError("SumatraPDF não encontrado em nenhum caminho padrão.");
                return PrintResult.Failed(PrintAttemptResult.UnknownError,
                    "SumatraPDF não encontrado. Instale o SumatraPDF ou atualize o caminho no appsettings.json");
            }
            
            _logger.LogInformation("SumatraPDF encontrado via auto-discovery em: {Path}", sumatraPath);
        }

        try
        {
            return await ExecuteSumatraPdfAsync(sumatraPath, pdfPath, printerName, copies, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return PrintResult.Failed(PrintAttemptResult.Timeout, "Impressão cancelada por timeout.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao imprimir via SumatraPDF.");
            return PrintResult.Failed(PrintAttemptResult.UnknownError, ex.Message);
        }
    }

    private async Task<PrintResult> ExecuteSumatraPdfAsync(
        string sumatraPath,
        string pdfPath,
        string printerName,
        int copies,
        CancellationToken cancellationToken)
    {
        // Argumentos do SumatraPDF:
        // -print-to "Printer Name"
        // -print-settings "1-4x,shrink" (copies)
        // -silent (no UI)
        
        var settings = copies > 1 ? $"{copies}x,shrink" : "shrink";
        var args = $"-print-to \"{printerName}\" -print-settings \"{settings}\" -silent \"{pdfPath}\"";

        _logger.LogDebug("Executando SumatraPDF: {Path} {Args}", sumatraPath, args);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = sumatraPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(pdfPath) ?? AppContext.BaseDirectory
        };

        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.PrintTimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return PrintResult.Failed(PrintAttemptResult.Timeout,
                $"SumatraPDF timeout após {_options.PrintTimeoutSeconds}s.");
        }

        if (process.ExitCode == 0)
        {
            _logger.LogInformation(
                "PDF enviado com sucesso para impressora '{Printer}' via SumatraPDF.",
                printerName);
            return PrintResult.Succeeded();
        }

        var errorMsg = $"SumatraPDF saiu com código {process.ExitCode}.";
        _logger.LogError("Falha no SumatraPDF: {Error}", errorMsg);

        return PrintResult.Failed(PrintAttemptResult.UnknownError, errorMsg);
    }
}
