using LabelPrinter.Core.Enums;

namespace LabelPrinter.Core.Entities;

/// <summary>
/// Representa um trabalho de impressão de etiqueta — unidade central do sistema.
/// Persiste todos os estados ao longo do pipeline de processamento.
/// </summary>
public class PrintJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Arquivo de entrada ---
    public string OriginalFilePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>SHA-256 do arquivo original recebido (para deduplicação).</summary>
    public string OriginalHash { get; set; } = string.Empty;

    /// <summary>SHA-256 do conteúdo normalizado (documento).</summary>
    public string? DocumentHash { get; set; }

    /// <summary>SHA-256 da etiqueta final renderizada.</summary>
    public string? LabelHash { get; set; }

    // --- Artefatos gerados ---
    public string? PdfPath { get; set; }
    public string? ArchivePath { get; set; }
    public string? ExtractedPath { get; set; }

    // --- Classificação ---
    public Marketplace Marketplace { get; set; } = Marketplace.Unknown;
    public LabelSize LabelSize { get; set; } = LabelSize.Unknown;
    public string? MarketplaceRaw { get; set; }

    // --- Roteamento ---
    public Guid? PrinterProfileId { get; set; }
    public PrinterProfile? PrinterProfile { get; set; }

    // --- Dados do pedido ---
    public string? OrderReference { get; set; }
    public string? TrackingNumber { get; set; }
    public int PackageNumber { get; set; } = 1;
    public int PackageCount { get; set; } = 1;

    // --- Estado e controle ---
    public JobStatus Status { get; set; } = JobStatus.Detected;
    public int AttemptCount { get; set; } = 0;
    public string? ErrorMessage { get; set; }
    public bool IsReprint { get; set; } = false;
    public bool IsRawData { get; set; } = false;
    public Guid? OriginalJobId { get; set; }

    // --- Metadados de tempo ---
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // --- Encoding ---
    public string? DetectedEncoding { get; set; }

    // --- Métricas de processamento ---
    public TimeSpan? ProcessingDuration => StartedAt.HasValue && CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;

    // --- Relacionamentos ---
    public ICollection<PrintAttempt> Attempts { get; set; } = new List<PrintAttempt>();
    public ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();

    /// <summary>Verifica se o job pode ser retentado baseado no número de tentativas e estado.</summary>
    public bool CanRetry(int maxAttempts) =>
        Status is JobStatus.Failed or JobStatus.Retrying &&
        AttemptCount < maxAttempts;

    /// <summary>Verifica se o job está em estado terminal (não pode mais mudar).</summary>
    public bool IsTerminal =>
        Status is JobStatus.Archived or JobStatus.Cancelled;

    /// <summary>Verifica se o job está aguardando ação ativa.</summary>
    public bool IsActive =>
        Status is JobStatus.Queued or JobStatus.Processing or JobStatus.Rendered
            or JobStatus.PdfGenerated or JobStatus.WaitingForPrinter
            or JobStatus.Printing or JobStatus.Retrying;
}
