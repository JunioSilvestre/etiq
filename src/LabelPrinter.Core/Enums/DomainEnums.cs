namespace LabelPrinter.Core.Enums;

/// <summary>
/// Marketplaces suportados para identificação automática de etiquetas.
/// </summary>
public enum Marketplace
{
    Unknown = 0,
    Amazon = 1,
    Shopee = 2,
    MercadoLivre = 3,
    Correios = 4,
    Jadlog = 5,
    Loggi = 6,
    MelhorEnvio = 7,
    Internal = 99
}

/// <summary>
/// Protocolo de impressão suportado pelo perfil da impressora.
/// </summary>
public enum PrinterProtocol
{
    /// <summary>Impressão via driver Windows GDI (PDF enviado pelo driver).</summary>
    WindowsGdi = 0,

    /// <summary>Impressão via GhostScript CLI com driver GDI.</summary>
    GhostScript = 1,

    /// <summary>Comandos RAW direto para a porta (ZPL, EPL, TSPL etc.).</summary>
    RawUsb = 2,

    /// <summary>ZPL/ZPL II enviado diretamente.</summary>
    Zpl = 3,

    /// <summary>EPL (Eltron Programming Language).</summary>
    Epl = 4,

    /// <summary>TSPL (TSC Printer Language).</summary>
    Tspl = 5,

    /// <summary>CPCL (Common Printer Command Language).</summary>
    Cpcl = 6,

    /// <summary>DPL (Datamax Programming Language).</summary>
    Dpl = 7,

    /// <summary>Simulado — não imprime, apenas gera artefatos.</summary>
    Simulated = 99
}

/// <summary>
/// Tamanhos padronizados de etiquetas suportados.
/// </summary>
public enum LabelSize
{
    Unknown = 0,
    /// <summary>100 × 150 mm — etiqueta logística padrão.</summary>
    Standard100x150 = 1,
    /// <summary>50 × 30 mm — etiqueta pequena (produto).</summary>
    Small50x30 = 2,
    /// <summary>Tamanho personalizado definido no template.</summary>
    Custom = 99
}

/// <summary>
/// Tipos de elemento gráfico presentes na etiqueta.
/// </summary>
public enum LabelElementType
{
    Text = 0,
    Barcode = 1,
    QrCode = 2,
    Image = 3,
    Line = 4,
    Box = 5
}

/// <summary>
/// Tipos de código de barras suportados.
/// </summary>
public enum BarcodeFormat
{
    Unknown = 0,
    Code128 = 1,
    Code39 = 2,
    Ean13 = 3,
    Ean8 = 4,
    Upc = 5,
    Itf = 6,
    Gs1128 = 7,
    DataMatrix = 8,
    QrCode = 9
}

/// <summary>
/// Resultado de tentativa de impressão.
/// </summary>
public enum PrintAttemptResult
{
    Success = 0,
    PrinterOffline = 1,
    PrinterNotFound = 2,
    SpoolerError = 3,
    Timeout = 4,
    InvalidPdf = 5,
    GhostScriptError = 6,
    UnknownError = 99
}

/// <summary>
/// Tipos de evento de auditoria.
/// </summary>
public enum AuditEventType
{
    JobCreated = 0,
    FileDetected = 1,
    FileStabilized = 2,
    ArchiveExtracted = 3,
    EncodingDetected = 4,
    MarketplaceDetected = 5,
    LabelParsed = 6,
    PdfGenerated = 7,
    PrintQueued = 8,
    PrintAttempted = 9,
    PrintSucceeded = 10,
    PrintFailed = 11,
    JobArchived = 12,
    JobFailed = 13,
    JobRetrying = 14,
    PrinterOffline = 15,
    PrinterOnline = 16,
    DuplicateDetected = 17,
    JobCancelled = 18,
    ReprintRequested = 19,
    ServiceStarted = 20,
    ServiceStopped = 21,
    RecoveryCompleted = 22
}
