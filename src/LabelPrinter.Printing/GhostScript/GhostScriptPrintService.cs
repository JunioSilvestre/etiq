using System.Diagnostics;
using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelPrinter.Printing.GhostScript;

/// <summary>
/// Envia PDF para impressão usando GhostScript CLI.
/// GhostScript oferece controle preciso de DPI, tamanho de papel e orientação.
/// Usado para impressoras com driver genérico (como Y43BT Label).
/// </summary>
public class GhostScriptPrintService : IWindowsPrintService
{
    private readonly ILogger<GhostScriptPrintService> _logger;
    private readonly PrintingOptions _options;

    public GhostScriptPrintService(
        ILogger<GhostScriptPrintService> logger,
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
            "Enviando PDF para impressão via GhostScript: {Pdf} → {Printer} ({Copies} cópia(s))",
            pdfPath, printerName, copies);

        if (!File.Exists(pdfPath))
            return PrintResult.Failed(PrintAttemptResult.InvalidPdf, $"PDF não encontrado: {pdfPath}");

        var gsPath = _options.GhostScriptPath;
        if (!File.Exists(gsPath))
        {
            _logger.LogError("GhostScript não encontrado em: {Path}", gsPath);
            return PrintResult.Failed(PrintAttemptResult.GhostScriptError,
                $"GhostScript não encontrado: {gsPath}. Instale o GhostScript e configure o caminho em appsettings.json");
        }

        try
        {
            var result = await ExecuteGhostScriptAsync(pdfPath, printerName, copies, cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            return PrintResult.Failed(PrintAttemptResult.Timeout, "Impressão cancelada por timeout.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao imprimir via GhostScript.");
            return PrintResult.Failed(PrintAttemptResult.UnknownError, ex.Message);
        }
    }

    private async Task<PrintResult> ExecuteGhostScriptAsync(
        string pdfPath,
        string printerName,
        int copies,
        CancellationToken cancellationToken)
    {
        // Argumentos do GhostScript para impressão via driver Windows
        // -dBATCH -dNOPAUSE: não interativo
        // -dPrinted: marca como impresso
        // -dNumCopies: número de cópias
        // -sOutputFile=%printer%NOME: envia para impressora Windows
        var args = string.Join(" ", [
            "-dBATCH",
            "-dNOPAUSE",
            "-dSAFER",
            "-dPrinted",
            $"-dNumCopies={copies}",
            "-sDEVICE=mswinpr2",
            $"-sOutputFile=\"%printer%{EscapeGsArg(printerName)}\"",
            $"\"{pdfPath}\""
        ]);

        _logger.LogDebug("Executando GhostScript: {Path} {Args}", _options.GhostScriptPath, args);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _options.GhostScriptPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(pdfPath) ?? AppContext.BaseDirectory
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.PrintTimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout interno
            try { process.Kill(entireProcessTree: true); } catch { }
            return PrintResult.Failed(PrintAttemptResult.Timeout,
                $"GhostScript timeout após {_options.PrintTimeoutSeconds}s.");
        }

        var stdout = await outputTask;
        var stderr = await errorTask;

        if (!string.IsNullOrWhiteSpace(stdout))
            _logger.LogDebug("GhostScript stdout: {Output}", stdout);

        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogWarning("GhostScript stderr: {Error}", stderr);

        if (process.ExitCode == 0)
        {
            _logger.LogInformation(
                "PDF enviado com sucesso para impressora '{Printer}' via GhostScript.",
                printerName);
            return PrintResult.Succeeded();
        }

        // Analisar código de saída do GhostScript
        var errorMsg = $"GhostScript saiu com código {process.ExitCode}. Stderr: {stderr}";
        _logger.LogError("Falha no GhostScript: {Error}", errorMsg);

        // Verificar se a impressora estava offline (código típico: 1)
        if (stderr.Contains("printer", StringComparison.OrdinalIgnoreCase) &&
            stderr.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return PrintResult.Failed(PrintAttemptResult.PrinterNotFound,
                $"Impressora '{printerName}' não encontrada pelo GhostScript.");
        }

        return PrintResult.Failed(PrintAttemptResult.GhostScriptError, errorMsg);
    }

    private static string EscapeGsArg(string value) =>
        value.Replace("\"", "\\\"");
}
