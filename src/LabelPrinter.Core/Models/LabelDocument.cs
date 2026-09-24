using LabelPrinter.Core.Enums;

namespace LabelPrinter.Core.Models;

/// <summary>
/// Modelo normalizado de uma etiqueta, independente do formato de entrada.
/// Todos os parsers produzem este modelo. Todos os renderers consomem este modelo.
/// </summary>
public class LabelDocument
{
    /// <summary>ID do job associado.</summary>
    public Guid JobId { get; set; }

    /// <summary>Marketplace de origem identificado.</summary>
    public Marketplace Marketplace { get; set; } = Marketplace.Unknown;

    /// <summary>Número do pedido / order reference.</summary>
    public string? OrderReference { get; set; }

    /// <summary>Código de rastreamento do envio.</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Nome da transportadora.</summary>
    public string? Carrier { get; set; }

    // --- Destinatário ---
    public string? RecipientName { get; set; }
    public string? RecipientDocument { get; set; }

    // --- Endereço de entrega ---
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AddressComplement { get; set; }
    public string? Neighborhood { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; } = "BR";

    // --- Remetente ---
    public string? SenderName { get; set; }
    public string? SenderDocument { get; set; }
    public string? SenderAddressLine1 { get; set; }
    public string? SenderCity { get; set; }
    public string? SenderState { get; set; }
    public string? SenderPostalCode { get; set; }

    // --- Códigos de barras ---
    public string? BarcodeValue { get; set; }
    public BarcodeFormat BarcodeFormat { get; set; } = BarcodeFormat.Unknown;

    public string? QrCodeValue { get; set; }

    // --- Dados do pacote ---
    public int PackageNumber { get; set; } = 1;
    public int PackageCount { get; set; } = 1;

    /// <summary>Peso declarado em gramas.</summary>
    public decimal? WeightGrams { get; set; }

    /// <summary>Indica se o arquivo original é um ZPL bruto que não deve ser renderizado para PDF.</summary>
    public bool IsRawZpl { get; set; } = false;

    // --- Tamanho da etiqueta ---
    public LabelSize LabelSize { get; set; } = LabelSize.Standard100x150;

    /// <summary>Largura personalizada em mm (quando LabelSize = Custom).</summary>
    public double? CustomWidthMm { get; set; }

    /// <summary>Altura personalizada em mm (quando LabelSize = Custom).</summary>
    public double? CustomHeightMm { get; set; }

    // --- Itens do pedido ---
    public IList<OrderItem> Items { get; set; } = new List<OrderItem>();

    // --- Campos adicionais não mapeados ---
    public IDictionary<string, string> AdditionalFields { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // --- Metadados de origem ---
    public string? SourceFileName { get; set; }
    public string? DetectedEncoding { get; set; }
    public string? RawContent { get; set; }
}

/// <summary>
/// Item de pedido associado à etiqueta.
/// </summary>
public class OrderItem
{
    public string? Sku { get; set; }
    public string? Description { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Informações sobre uma impressora descoberta no Windows.
/// </summary>
public class PrinterInfo
{
    public string Name { get; set; } = string.Empty;
    public string? DriverName { get; set; }
    public string? PortName { get; set; }
    public bool IsDefault { get; set; }
    public bool IsOnline { get; set; }
    public int? PrinterStatus { get; set; }
    public string? StatusDescription { get; set; }
    public int JobCount { get; set; }
    public string? PrintProcessor { get; set; }
    public int? HorizontalResolution { get; set; }
    public int? VerticalResolution { get; set; }
    public bool IsDuplicate { get; set; }
}

/// <summary>
/// Resultado de uma operação de processamento de job.
/// </summary>
public class ProcessingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsPermanentError { get; set; }
    public LabelDocument? Document { get; set; }
    public string? PdfPath { get; set; }

    public static ProcessingResult Succeeded(LabelDocument document, string pdfPath) =>
        new() { Success = true, Document = document, PdfPath = pdfPath };

    public static ProcessingResult TemporaryFailure(string error) =>
        new() { Success = false, ErrorMessage = error, IsPermanentError = false };

    public static ProcessingResult PermanentFailure(string error) =>
        new() { Success = false, ErrorMessage = error, IsPermanentError = true };
}

/// <summary>
/// Resultado de uma operação de impressão.
/// </summary>
public class PrintResult
{
    public bool Success { get; set; }
    public PrintAttemptResult AttemptResult { get; set; }
    public string? ErrorMessage { get; set; }
    public int? SpoolerJobId { get; set; }

    public static PrintResult Succeeded(int? spoolerJobId = null) =>
        new() { Success = true, AttemptResult = PrintAttemptResult.Success, SpoolerJobId = spoolerJobId };

    public static PrintResult Failed(PrintAttemptResult result, string error) =>
        new() { Success = false, AttemptResult = result, ErrorMessage = error };
}
