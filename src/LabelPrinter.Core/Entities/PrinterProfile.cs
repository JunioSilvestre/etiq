using LabelPrinter.Core.Enums;

namespace LabelPrinter.Core.Entities;

/// <summary>
/// Perfil de configuração de uma impressora.
/// Cada impressora instalada pode ter um ou mais perfis configurados.
/// Nunca hardcoda o nome da impressora — descobre dinamicamente via WMI.
/// </summary>
public class PrinterProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nome amigável interno da configuração (ex: "Térmica Principal").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Descrição livre.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Nome exato da impressora no Windows (Win32_Printer.Name).
    /// Configurado via appsettings.json e validado no startup contra o WMI.
    /// </summary>
    public string WindowsPrinterName { get; set; } = string.Empty;

    /// <summary>Nome do driver conforme retornado pelo WMI.</summary>
    public string? DriverName { get; set; }

    /// <summary>Porta da impressora (USB001, COM1, etc.).</summary>
    public string? PortName { get; set; }

    /// <summary>Protocolo de impressão preferido.</summary>
    public PrinterProtocol Protocol { get; set; } = PrinterProtocol.GhostScript;

    /// <summary>DPI da impressora. Configurável pois nem sempre é possível detectar via WMI.</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>Largura padrão da etiqueta em mm.</summary>
    public double DefaultLabelWidthMm { get; set; } = 100.0;

    /// <summary>Altura padrão da etiqueta em mm.</summary>
    public double DefaultLabelHeightMm { get; set; } = 150.0;

    /// <summary>Indica se este perfil está habilitado.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Prioridade de seleção (menor número = maior prioridade).</summary>
    public int Priority { get; set; } = 100;

    /// <summary>Número máximo de tentativas para este perfil.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Delay entre tentativas em segundos.</summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>Timeout de impressão em segundos.</summary>
    public int PrintTimeoutSeconds { get; set; } = 60;

    /// <summary>Número de cópias padrão.</summary>
    public int DefaultCopies { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Relacionamentos ---
    public ICollection<PrintJob> Jobs { get; set; } = new List<PrintJob>();
    public ICollection<PrinterRoutingRule> RoutingRules { get; set; } = new List<PrinterRoutingRule>();
}
