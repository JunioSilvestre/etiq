using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Exceptions;
using LabelPrinter.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace LabelPrinter.Documents.Extractors;

/// <summary>
/// Extrai arquivos compactados com proteções de segurança completas.
/// Suporta: ZIP, 7Z, TAR, GZ via SharpCompress.
/// Protege contra: ZIP bomb, path traversal, tamanho excessivo, extensões inválidas.
/// </summary>
public class ArchiveExtractor : IArchiveExtractor
{
    private readonly ILogger<ArchiveExtractor> _logger;
    private readonly SecurityOptions _security;

    public ArchiveExtractor(
        ILogger<ArchiveExtractor> logger,
        IOptions<SecurityOptions> security)
    {
        _logger = logger;
        _security = security.Value;
    }

    public async Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Extraindo arquivo: {Archive} → {Output}", archivePath, outputDirectory);

        var fileInfo = new FileInfo(archivePath);
        if (!fileInfo.Exists)
            return ArchiveExtractionResult.Failed($"Arquivo não encontrado: {archivePath}");

        if (fileInfo.Length > _security.MaxArchiveSizeBytes)
            return ArchiveExtractionResult.Failed(
                $"Arquivo muito grande: {fileInfo.Length} bytes (máximo: {_security.MaxArchiveSizeBytes} bytes)");

        Directory.CreateDirectory(outputDirectory);
        var absoluteOutputDir = Path.GetFullPath(outputDirectory);

        var extractedFiles = new List<string>();
        long totalExtractedBytes = 0;
        int fileCount = 0;

        try
        {
            await Task.Run(() =>
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);

                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    fileCount++;
                    if (fileCount > _security.MaxFilesInArchive)
                        throw new ZipBombException(
                            $"Arquivo contém mais de {_security.MaxFilesInArchive} arquivos (possível ZIP bomb).");

                    if (entry.Size > _security.MaxExtractedSizeBytes)
                        throw new ZipBombException(
                            $"Entrada '{entry.Key}' excede tamanho máximo: {entry.Size} bytes.");

                    totalExtractedBytes += entry.Size;
                    if (totalExtractedBytes > _security.MaxExtractedSizeBytes)
                        throw new ZipBombException(
                            $"Tamanho total extraído excede limite: {totalExtractedBytes} bytes.");

                    // Validar extensão
                    var extension = Path.GetExtension(entry.Key ?? string.Empty).ToLowerInvariant();
                    var allowedExts = _security.AllowedExtractedExtensions;
                    if (!allowedExts.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogWarning("Extensão não permitida: {Ext} ({File})", extension, entry.Key);
                        continue;
                    }

                    // Validar path traversal
                    var sanitizedPath = SanitizeEntryPath(entry.Key ?? $"file_{fileCount}{extension}");
                    var fullOutputPath = Path.GetFullPath(Path.Combine(absoluteOutputDir, sanitizedPath));

                    if (!fullOutputPath.StartsWith(absoluteOutputDir, StringComparison.OrdinalIgnoreCase))
                        throw new PathTraversalException(
                            $"Path traversal detectado na entrada: '{entry.Key}'");

                    var entryDirectory = Path.GetDirectoryName(fullOutputPath);
                    if (!string.IsNullOrEmpty(entryDirectory))
                        Directory.CreateDirectory(entryDirectory);

                    entry.WriteToFile(fullOutputPath, new ExtractionOptions
                    {
                        ExtractFullPath = false,
                        Overwrite = true
                    });

                    extractedFiles.Add(fullOutputPath);
                    _logger.LogDebug("Extraído: {File} ({Size} bytes)", sanitizedPath, entry.Size);
                }
            }, cancellationToken);

            _logger.LogInformation(
                "Extração concluída: {Count} arquivo(s), {TotalBytes} bytes",
                extractedFiles.Count, totalExtractedBytes);

            return ArchiveExtractionResult.Succeeded(outputDirectory, extractedFiles, totalExtractedBytes);
        }
        catch (Exception ex) when (ex is ZipBombException or PathTraversalException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao extrair arquivo: {Archive}", archivePath);
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, recursive: true);
            }
            catch { }
            return ArchiveExtractionResult.Failed($"Erro ao extrair: {ex.Message}");
        }
    }

    private static string SanitizeEntryPath(string entryPath)
    {
        entryPath = entryPath.Replace('\\', '/');
        var parts = entryPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != ".." && p != "." && !string.IsNullOrWhiteSpace(p))
            .Select(p => SanitizeFileName(p));
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
