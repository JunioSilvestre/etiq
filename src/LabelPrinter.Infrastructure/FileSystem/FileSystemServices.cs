using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelPrinter.Infrastructure.FileSystem;

/// <summary>
/// Verifica se um arquivo está completamente baixado e pronto para processamento.
/// Implementa polling de tamanho + verificação de lock de arquivo.
/// Nunca processa arquivos parcialmente baixados.
/// </summary>
public class FileStabilityChecker : IFileStabilityChecker
{
    private readonly ILogger<FileStabilityChecker> _logger;
    private readonly FileStabilityOptions _options;

    public FileStabilityChecker(
        ILogger<FileStabilityChecker> logger,
        IOptions<FileStabilityOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> WaitForStabilityAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Aguardando estabilidade do arquivo: {FilePath}", filePath);

        var timeoutAt = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds);
        long previousSize = -1;
        int stableCount = 0;

        while (DateTime.UtcNow < timeoutAt && !cancellationToken.IsCancellationRequested)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Arquivo não existe mais: {FilePath}", filePath);
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            var currentSize = fileInfo.Length;

            if (currentSize < _options.MinFileSizeBytes)
            {
                _logger.LogDebug("Arquivo muito pequeno ({Size} bytes): {FilePath}. Aguardando...", currentSize, filePath);
                await Task.Delay(_options.CheckIntervalMs, cancellationToken);
                continue;
            }

            if (IsFileLocked(filePath))
            {
                _logger.LogDebug("Arquivo bloqueado: {FilePath}. Aguardando...", filePath);
                stableCount = 0;
                previousSize = currentSize;
                await Task.Delay(_options.CheckIntervalMs, cancellationToken);
                continue;
            }

            if (currentSize == previousSize)
            {
                stableCount++;
                _logger.LogDebug(
                    "Tamanho estável ({Size} bytes): {FilePath}. Confirmações: {Count}/{Required}",
                    currentSize, filePath, stableCount, _options.CheckCount);

                if (stableCount >= _options.CheckCount)
                {
                    _logger.LogInformation(
                        "Arquivo estabilizado: {FilePath} ({Size} bytes).",
                        filePath, currentSize);
                    return true;
                }
            }
            else
            {
                if (previousSize != -1)
                {
                    _logger.LogDebug(
                        "Tamanho mudou de {Previous} para {Current} bytes: {FilePath}. Reiniciando contagem.",
                        previousSize, currentSize, filePath);
                }
                stableCount = 0;
            }

            previousSize = currentSize;
            await Task.Delay(_options.CheckIntervalMs, cancellationToken);
        }

        _logger.LogWarning(
            "Timeout aguardando estabilidade do arquivo: {FilePath}. Timeout: {Timeout}s",
            filePath, _options.TimeoutSeconds);
        return false;
    }

    public bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None, // Tenta abrir exclusivo — se falhar, está bloqueado
                1,
                FileOptions.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>
/// Detecta o formato de arquivo baseado em magic bytes (bytes mágicos) e extensão.
/// Não confia apenas na extensão — verifica o conteúdo real.
/// </summary>
public class FormatDetector : IFormatDetector
{
    // Magic bytes de formatos conhecidos
    private static readonly Dictionary<byte[], string> MagicBytes = new()
    {
        { [0x50, 0x4B, 0x03, 0x04], "zip" },         // ZIP (PK\x03\x04)
        { [0x50, 0x4B, 0x05, 0x06], "zip" },          // ZIP empty
        { [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], "7z" }, // 7-Zip
        { [0x1F, 0x8B], "gz" },                        // GZip
        { [0x42, 0x5A, 0x68], "bz2" },                 // BZip2
        { [0x75, 0x73, 0x74, 0x61, 0x72], "tar" },     // TAR (offset 257, mas verifica early)
        { [0x25, 0x50, 0x44, 0x46], "pdf" },           // PDF (%PDF)
        { [0xFF, 0xFE], "utf16le" },                   // UTF-16 LE BOM (texto)
        { [0xFE, 0xFF], "utf16be" },                   // UTF-16 BE BOM (texto)
        { [0xEF, 0xBB, 0xBF], "utf8bom" },             // UTF-8 BOM (texto)
    };

    private static readonly Dictionary<string, string> ExtensionFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".zip", "zip" },
        { ".7z", "7z" },
        { ".tar", "tar" },
        { ".gz", "gz" },
        { ".bz2", "bz2" },
        { ".txt", "txt" },
        { ".csv", "csv" },
        { ".pdf", "pdf" },
        { ".zpl", "zpl" },
        { ".epl", "epl" },
        { ".cpcl", "cpcl" },
        { ".tspl", "tspl" },
        { ".json", "json" },
        { ".xml", "xml" }
    };

    private static readonly HashSet<string> ArchiveFormats = ["zip", "7z", "tar", "gz", "bz2"];
    private static readonly HashSet<string> LabelFormats = ["zpl", "epl", "cpcl", "tspl", "txt", "csv"];

    public string DetectFormat(string filePath)
    {
        // Primeiro tenta pelos magic bytes (mais confiável)
        try
        {
            var buffer = new byte[8];
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8);
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var byFormat = DetectFormat(buffer);
                if (byFormat != "unknown")
                    return byFormat;
            }
        }
        catch { /* fallback to extension */ }

        // Fallback: extensão
        var ext = Path.GetExtension(filePath);
        if (ExtensionFormats.TryGetValue(ext, out var format))
            return format;

        return "unknown";
    }

    public string DetectFormat(byte[] header)
    {
        foreach (var (magic, format) in MagicBytes)
        {
            if (header.Length >= magic.Length && header.Take(magic.Length).SequenceEqual(magic))
                return format;
        }
        return "unknown";
    }

    public bool IsArchive(string format) => ArchiveFormats.Contains(format.ToLowerInvariant());
    public bool IsLabelFormat(string format) => LabelFormats.Contains(format.ToLowerInvariant());
}
