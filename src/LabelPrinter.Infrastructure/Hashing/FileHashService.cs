using System.Security.Cryptography;
using LabelPrinter.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LabelPrinter.Infrastructure.Hashing;

/// <summary>
/// Calcula hashes SHA-256 de arquivos para deduplicação de jobs.
/// Usa streaming para não carregar arquivos grandes em memória.
/// </summary>
public class FileHashService : IFileHashService
{
    private readonly ILogger<FileHashService> _logger;

    public FileHashService(ILogger<FileHashService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calculando SHA-256 para arquivo: {FilePath}", filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920, // 80 KB buffer
            useAsync: true);

        return await ComputeSha256Async(stream, cancellationToken);
    }

    public async Task<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public Task<string> ComputeSha256Async(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Task.FromResult(Convert.ToHexString(hashBytes).ToLowerInvariant());
    }
}
