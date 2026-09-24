using LabelPrinter.Core.Models;

namespace LabelPrinter.Core.Interfaces;

/// <summary>
/// Argumentos do evento de arquivo detectado.
/// </summary>
public class FileDetectedEventArgs : EventArgs
{
    public string FilePath { get; }
    public string FileName { get; }
    public DateTime DetectedAt { get; }

    public FileDetectedEventArgs(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        DetectedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Resultado da extração de um arquivo compactado.
/// </summary>
public class ArchiveExtractionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public IList<string> ExtractedFiles { get; set; } = new List<string>();
    public long TotalExtractedBytes { get; set; }

    public static ArchiveExtractionResult Succeeded(string outputDir, IList<string> files, long totalBytes) =>
        new() { Success = true, OutputDirectory = outputDir, ExtractedFiles = files, TotalExtractedBytes = totalBytes };

    public static ArchiveExtractionResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// Resultado da validação de um PDF antes da impressão.
/// </summary>
public class PdfValidationResult
{
    public bool IsValid { get; set; }
    public IList<string> Errors { get; set; } = new List<string>();
    public IList<string> Warnings { get; set; } = new List<string>();
    public int PageCount { get; set; }
    public double? PageWidthMm { get; set; }
    public double? PageHeightMm { get; set; }

    public static PdfValidationResult Valid(int pages, double widthMm, double heightMm) =>
        new() { IsValid = true, PageCount = pages, PageWidthMm = widthMm, PageHeightMm = heightMm };

    public static PdfValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors.ToList() };
}

/// <summary>
/// Métricas do sistema para observabilidade.
/// </summary>
public class SystemMetrics
{
    public long JobsReceived { get; set; }
    public long JobsProcessed { get; set; }
    public long JobsFailed { get; set; }
    public long JobsPrinted { get; set; }
    public long JobsRetried { get; set; }
    public double AverageProcessingSeconds { get; set; }
    public double AveragePrintingSeconds { get; set; }
    public long PrinterOfflineCount { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
