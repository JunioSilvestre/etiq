using LabelPrinter.Core.Entities;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Models;

namespace LabelPrinter.Core.Interfaces;

// ============================================================
// Printer Discovery & Management
// ============================================================

/// <summary>
/// Descobre impressoras instaladas via WMI (Windows Management Instrumentation).
/// Nunca assume nomes ou drivers — consulta o sistema.
/// </summary>
public interface IPrinterDiscoveryService
{
    /// <summary>Retorna todas as impressoras instaladas no Windows.</summary>
    Task<IReadOnlyList<PrinterInfo>> DiscoverPrintersAsync(CancellationToken cancellationToken = default);

    /// <summary>Retorna informações de uma impressora específica pelo nome.</summary>
    Task<PrinterInfo?> GetPrinterInfoAsync(string printerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filtra impressoras candidatas a serem térmicas com base no driver e porta.
    /// Detecta e marca cópias duplicadas na mesma porta.
    /// </summary>
    Task<IReadOnlyList<PrinterInfo>> GetThermalPrinterCandidatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifica a saúde e disponibilidade das impressoras.
/// </summary>
public interface IPrinterHealthService
{
    Task<bool> IsPrinterOnlineAsync(string printerName, CancellationToken cancellationToken = default);
    Task<int> GetJobCountAsync(string printerName, CancellationToken cancellationToken = default);
    Task<string> GetStatusDescriptionAsync(string printerName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gerencia perfis de impressora — sincroniza configuração com banco de dados.
/// </summary>
public interface IPrinterProfileService
{
    Task<IReadOnlyList<PrinterProfile>> GetEnabledProfilesAsync(CancellationToken cancellationToken = default);
    Task<PrinterProfile?> GetProfileByNameAsync(string windowsPrinterName, CancellationToken cancellationToken = default);
    Task SyncProfilesFromConfigAsync(CancellationToken cancellationToken = default);
    Task<PrinterProfile?> SelectPrinterForJobAsync(PrintJob job, CancellationToken cancellationToken = default);
}

// ============================================================
// File System & Monitoring
// ============================================================

/// <summary>
/// Monitora a pasta Downloads por novos arquivos.
/// Combina FileSystemWatcher com polling periódico como fallback.
/// </summary>
public interface IFileWatcherService
{
    /// <summary>Inicia o monitoramento.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Para o monitoramento graciosamente.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Evento disparado quando um novo arquivo estável for detectado.</summary>
    event EventHandler<FileDetectedEventArgs>? FileDetected;
}

/// <summary>
/// Verifica se um arquivo está completamente baixado e não está sendo escrito.
/// </summary>
public interface IFileStabilityChecker
{
    /// <summary>
    /// Aguarda o arquivo estabilizar (download completo).
    /// Retorna true se estável, false se timeout ou arquivo inválido.
    /// </summary>
    Task<bool> WaitForStabilityAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Verifica se o arquivo está bloqueado por outro processo.</summary>
    bool IsFileLocked(string filePath);
}

/// <summary>
/// Calcula e verifica hashes de arquivos para deduplicação.
/// </summary>
public interface IFileHashService
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
    Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default);
    Task<string> ComputeSha256Async(byte[] data);
}

// ============================================================
// Document Processing
// ============================================================

/// <summary>
/// Detecta o formato de um arquivo baseado em magic bytes e extensão.
/// </summary>
public interface IFormatDetector
{
    string DetectFormat(string filePath);
    string DetectFormat(byte[] header);
    bool IsArchive(string format);
    bool IsLabelFormat(string format);
}

/// <summary>
/// Extrai arquivos compactados com validações de segurança.
/// </summary>
public interface IArchiveExtractor
{
    /// <summary>
    /// Extrai o arquivo para um diretório temporário isolado.
    /// Valida: ZIP bomb, path traversal, tamanho máximo, extensões.
    /// </summary>
    Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Detecta o encoding de um arquivo de texto.
/// </summary>
public interface IEncodingDetector
{
    System.Text.Encoding DetectEncoding(string filePath);
    System.Text.Encoding DetectEncoding(byte[] data);
    string DetectEncodingName(string filePath);
}

/// <summary>
/// Detecta o marketplace de origem baseado em nome de arquivo e conteúdo.
/// </summary>
public interface IMarketplaceDetector
{
    Marketplace DetectMarketplace(string fileName, string? content = null);
}

/// <summary>
/// Parseia um documento/arquivo e produz um LabelDocument normalizado.
/// Cada implementação suporta um tipo de entrada específico.
/// </summary>
public interface ILabelParser
{
    /// <summary>Indica se este parser consegue processar o arquivo.</summary>
    bool CanParse(string filePath, string? content = null);

    /// <summary>Realiza o parsing e retorna o modelo normalizado.</summary>
    Task<LabelDocument?> ParseAsync(
        string filePath,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default);

    /// <summary>Nome descritivo do parser para logging.</summary>
    string ParserName { get; }
}

// ============================================================
// Rendering & PDF
// ============================================================

/// <summary>
/// Template de configuração visual de uma etiqueta.
/// </summary>
public interface ILabelTemplate
{
    string TemplateName { get; }
    double WidthMm { get; }
    double HeightMm { get; }
    double MarginMm { get; }
}

/// <summary>
/// Renderiza um LabelDocument para um formato de saída (PDF, imagem, RAW).
/// </summary>
public interface ILabelRenderer
{
    /// <summary>Renderiza e retorna o conteúdo em bytes.</summary>
    Task<byte[]> RenderAsync(
        LabelDocument document,
        ILabelTemplate template,
        CancellationToken cancellationToken = default);

    string RendererName { get; }
}

/// <summary>
/// Gera PDF de etiqueta e salva em disco.
/// </summary>
public interface IPdfGeneratorService
{
    Task<string> GeneratePdfAsync(
        LabelDocument document,
        ILabelTemplate template,
        string outputPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Valida um PDF gerado antes de enviá-lo para impressão.
/// </summary>
public interface IPdfValidatorService
{
    Task<PdfValidationResult> ValidateAsync(string pdfPath, ILabelTemplate template, CancellationToken cancellationToken = default);
}

// ============================================================
// Printing
// ============================================================

/// <summary>
/// Envia um trabalho de impressão para o Windows Spooler.
/// </summary>
public interface IWindowsPrintService
{
    Task<PrintResult> PrintAsync(
        string pdfPath,
        string printerName,
        int copies = 1,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Gerencia a fila de impressão para múltiplas impressoras simultâneas.
/// Garante ordem FIFO por impressora e evita duplicação.
/// </summary>
public interface IPrintQueueService
{
    Task EnqueueAsync(PrintJob job, CancellationToken cancellationToken = default);
    Task<PrintJob?> DequeueAsync(string printerName, CancellationToken cancellationToken = default);
    Task<int> GetQueueLengthAsync(string printerName);
}

// ============================================================
// Job Management
// ============================================================

/// <summary>
/// Gerencia criação e atualização de jobs, incluindo deduplicação.
/// </summary>
public interface IJobService
{
    Task<PrintJob?> CreateJobAsync(string filePath, string fileHash, CancellationToken cancellationToken = default);
    Task<bool> IsDuplicateAsync(string fileHash, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid jobId, Enums.JobStatus status, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task<PrintJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrintJob>> GetRecoverableJobsAsync(CancellationToken cancellationToken = default);
    Task<PrintJob> ReprintJobAsync(Guid originalJobId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline completo de processamento de um job.
/// </summary>
public interface IJobProcessingPipeline
{
    Task<ProcessingResult> ProcessAsync(PrintJob job, CancellationToken cancellationToken = default);
}

// ============================================================
// Repositories
// ============================================================

public interface IJobRepository
{
    Task<PrintJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PrintJob?> GetByHashAsync(string hash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrintJob>> GetByStatusAsync(Enums.JobStatus status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrintJob>> GetActiveJobsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PrintJob job, CancellationToken cancellationToken = default);
    Task UpdateAsync(PrintJob job, CancellationToken cancellationToken = default);
}

public interface IPrinterProfileRepository
{
    Task<IReadOnlyList<PrinterProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrinterProfile>> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task<PrinterProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(PrinterProfile profile, CancellationToken cancellationToken = default);
}

public interface IAuditRepository
{
    Task AddEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task AddPrintAttemptAsync(PrintAttempt attempt, CancellationToken cancellationToken = default);
}

// ============================================================
// Observability
// ============================================================

public interface IMetricsService
{
    void IncrementJobsReceived();
    void IncrementJobsProcessed();
    void IncrementJobsFailed();
    void IncrementJobsPrinted();
    void IncrementJobsRetried();
    void RecordProcessingDuration(TimeSpan duration);
    void RecordPrintingDuration(TimeSpan duration);
    void IncrementPrinterOfflineCount(string printerName);
    Task<SystemMetrics> GetMetricsAsync();
}
