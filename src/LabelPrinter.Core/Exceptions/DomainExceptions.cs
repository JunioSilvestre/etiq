namespace LabelPrinter.Core.Exceptions;

/// <summary>
/// Erro permanente — o job não deve ser retentado.
/// </summary>
public class PermanentProcessingException : Exception
{
    public PermanentProcessingException(string message) : base(message) { }
    public PermanentProcessingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Erro temporário — o job pode ser retentado após um delay.
/// </summary>
public class TemporaryProcessingException : Exception
{
    public TemporaryProcessingException(string message) : base(message) { }
    public TemporaryProcessingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Impressora não encontrada no sistema.
/// </summary>
public class PrinterNotFoundException : Exception
{
    public string PrinterName { get; }
    public PrinterNotFoundException(string printerName)
        : base($"Impressora não encontrada: '{printerName}'")
    {
        PrinterName = printerName;
    }
}

/// <summary>
/// Impressora offline ou indisponível.
/// </summary>
public class PrinterOfflineException : Exception
{
    public string PrinterName { get; }
    public PrinterOfflineException(string printerName)
        : base($"Impressora offline: '{printerName}'")
    {
        PrinterName = printerName;
    }
}

/// <summary>
/// Arquivo ZIP inválido ou corrompido.
/// </summary>
public class InvalidArchiveException : Exception
{
    public InvalidArchiveException(string message) : base(message) { }
    public InvalidArchiveException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Arquivo ZIP bomb detectado (proteção de segurança).
/// </summary>
public class ZipBombException : Exception
{
    public ZipBombException(string message) : base(message) { }
}

/// <summary>
/// Arquivo com path traversal detectado (proteção de segurança).
/// </summary>
public class PathTraversalException : Exception
{
    public PathTraversalException(string message) : base(message) { }
}

/// <summary>
/// Erro ao gerar PDF.
/// </summary>
public class PdfGenerationException : Exception
{
    public PdfGenerationException(string message) : base(message) { }
    public PdfGenerationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Encoding detectado mas não suportado.
/// </summary>
public class UnsupportedEncodingException : Exception
{
    public UnsupportedEncodingException(string message) : base(message) { }
}

/// <summary>
/// Job duplicado detectado (mesmo hash já processado).
/// </summary>
public class DuplicateJobException : Exception
{
    public Guid ExistingJobId { get; }
    public DuplicateJobException(Guid existingJobId, string hash)
        : base($"Job duplicado detectado. Hash={hash}, Job existente={existingJobId}")
    {
        ExistingJobId = existingJobId;
    }
}
