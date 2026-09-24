using LabelPrinter.Core.Interfaces;
using Microsoft.Extensions.Logging;
using UtfUnknown;

namespace LabelPrinter.Infrastructure.Encoding;

/// <summary>
/// Detecta encoding de arquivos de texto usando a biblioteca UtfUnknown.
/// Suporta: UTF-8, UTF-8 BOM, UTF-16 LE/BE, Windows-1252, ISO-8859-1.
/// Nunca assume UTF-8 — sempre detecta ou usa fallback configurado.
/// </summary>
public class EncodingDetectorService : IEncodingDetector
{
    private readonly ILogger<EncodingDetectorService> _logger;

    // Encoding de fallback quando não é possível detectar com confiança
    private static readonly System.Text.Encoding FallbackEncoding =
        System.Text.Encoding.GetEncoding("windows-1252");

    public EncodingDetectorService(ILogger<EncodingDetectorService> logger)
    {
        _logger = logger;

        // Registrar provider para encodings como windows-1252
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public System.Text.Encoding DetectEncoding(string filePath)
    {
        try
        {
            var result = CharsetDetector.DetectFromFile(filePath);
            return ResolveEncoding(result, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao detectar encoding de {FilePath}. Usando fallback: {Fallback}.",
                filePath, FallbackEncoding.EncodingName);
            return FallbackEncoding;
        }
    }

    public System.Text.Encoding DetectEncoding(byte[] data)
    {
        try
        {
            var result = CharsetDetector.DetectFromBytes(data);
            return ResolveEncoding(result, "byte[]");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao detectar encoding de bytes. Usando fallback: {Fallback}.",
                FallbackEncoding.EncodingName);
            return FallbackEncoding;
        }
    }

    public string DetectEncodingName(string filePath)
    {
        var encoding = DetectEncoding(filePath);
        return encoding.WebName.ToUpperInvariant();
    }

    private System.Text.Encoding ResolveEncoding(DetectionResult result, string source)
    {
        var detected = result.Detected;

        if (detected == null || detected.Confidence < 0.5f)
        {
            _logger.LogWarning(
                "Encoding não detectado com confiança suficiente para {Source} " +
                "(confiança: {Confidence:P1}). Usando fallback: {Fallback}.",
                source,
                detected?.Confidence ?? 0,
                FallbackEncoding.EncodingName);
            return FallbackEncoding;
        }

        _logger.LogDebug(
            "Encoding detectado para {Source}: {Encoding} (confiança: {Confidence:P1}).",
            source, detected.EncodingName, detected.Confidence);

        try
        {
            // UtfUnknown retorna nomes como "UTF-8", "UTF-16 LE", "windows-1252"
            var encodingName = NormalizeEncodingName(detected.EncodingName);
            var encoding = System.Text.Encoding.GetEncoding(encodingName);
            _logger.LogInformation("Encoding resolvido: {Name} para {Source}.", encoding.WebName, source);
            return encoding;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "Encoding detectado '{Name}' não suportado pelo .NET. Usando fallback: {Fallback}.",
                detected.EncodingName, FallbackEncoding.EncodingName);
            return FallbackEncoding;
        }
    }

    private static string NormalizeEncodingName(string name) => name.Trim().ToLowerInvariant() switch
    {
        "utf-8" => "utf-8",
        "utf-8 bom" or "utf-8-bom" => "utf-8",
        "utf-16 le" or "utf-16le" => "utf-16",
        "utf-16 be" or "utf-16be" => "utf-16BE",
        "windows-1252" or "win-1252" or "cp1252" => "windows-1252",
        "iso-8859-1" or "latin-1" or "latin1" => "iso-8859-1",
        "ascii" => "us-ascii",
        var other => other
    };
}
