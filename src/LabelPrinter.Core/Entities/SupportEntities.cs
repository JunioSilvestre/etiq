using LabelPrinter.Core.Enums;

namespace LabelPrinter.Core.Entities;

/// <summary>
/// Regra de roteamento para determinar qual impressora recebe determinado job.
/// Permite configurar: Amazon → Printer A, Shopee → Printer B, etc.
/// </summary>
public class PrinterRoutingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PrinterProfileId { get; set; }
    public PrinterProfile? PrinterProfile { get; set; }

    /// <summary>Marketplace alvo. null = qualquer marketplace.</summary>
    public Marketplace? Marketplace { get; set; }

    /// <summary>Tamanho de etiqueta alvo. null = qualquer tamanho.</summary>
    public LabelSize? LabelSize { get; set; }

    /// <summary>Prioridade da regra (menor = mais prioritária).</summary>
    public int Priority { get; set; } = 100;

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Impressora descoberta via WMI no sistema Windows.
/// Registra todas as impressoras encontradas, independente de terem perfil configurado.
/// </summary>
public class DiscoveredPrinter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nome exato conforme retornado pelo WMI (Win32_Printer.Name).</summary>
    public string WindowsName { get; set; } = string.Empty;

    public string? DriverName { get; set; }
    public string? PortName { get; set; }
    public bool IsDefault { get; set; }
    public bool IsOnline { get; set; }
    public string? Status { get; set; }
    public int? PrinterStatus { get; set; }

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Registro de tentativa de impressão para um job específico.
/// </summary>
public class PrintAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }
    public PrintJob? Job { get; set; }

    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public PrintAttemptResult Result { get; set; } = PrintAttemptResult.UnknownError;
    public string? ErrorMessage { get; set; }

    /// <summary>ID do job no Windows Spooler, quando disponível.</summary>
    public int? SpoolerJobId { get; set; }

    /// <summary>Nome da impressora usada nesta tentativa.</summary>
    public string? PrinterName { get; set; }
}

/// <summary>
/// Evento de auditoria para rastreabilidade completa do job.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? JobId { get; set; }
    public PrintJob? Job { get; set; }

    public AuditEventType EventType { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Metadados adicionais serializados como JSON.</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configurações persistidas no banco de dados.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
