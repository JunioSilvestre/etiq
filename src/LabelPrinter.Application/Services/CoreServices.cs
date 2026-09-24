using System.Collections.Concurrent;
using System.Threading.Channels;
using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Entities;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelPrinter.Application.Services;

/// <summary>
/// Gerencia a fila de impressão.
/// Usa Channel<T> por impressora para garantir ordem FIFO e controle de concorrência.
/// Cada impressora tem seu próprio Channel independente.
/// </summary>
public class PrintQueueService : IPrintQueueService
{
    private readonly ILogger<PrintQueueService> _logger;
    private readonly ProcessingOptions _options;
    private readonly ConcurrentDictionary<string, Channel<PrintJob>> _queues = new(StringComparer.OrdinalIgnoreCase);

    public PrintQueueService(
        ILogger<PrintQueueService> logger,
        IOptions<ProcessingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task EnqueueAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        if (job.PrinterProfile == null)
        {
            _logger.LogError("Job {JobId} não tem PrinterProfile configurado.", job.Id);
            return;
        }

        var printerName = job.PrinterProfile.WindowsPrinterName;
        var channel = GetOrCreateChannel(printerName);

        await channel.Writer.WriteAsync(job, cancellationToken);
        _logger.LogInformation("Job {JobId} enfileirado para impressora: {Printer}", job.Id, printerName);
    }

    public async Task<PrintJob?> DequeueAsync(string printerName, CancellationToken cancellationToken = default)
    {
        var channel = GetOrCreateChannel(printerName);

        if (await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            if (channel.Reader.TryRead(out var job))
                return job;
        }

        return null;
    }

    public Task<int> GetQueueLengthAsync(string printerName)
    {
        if (_queues.TryGetValue(printerName, out var channel))
            return Task.FromResult(channel.Reader.Count);
        return Task.FromResult(0);
    }

    private Channel<PrintJob> GetOrCreateChannel(string printerName)
    {
        return _queues.GetOrAdd(printerName, _ =>
        {
            _logger.LogInformation("Criando fila de impressão para: {Printer}", printerName);
            return Channel.CreateBounded<PrintJob>(new BoundedChannelOptions(500)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,  // Um worker por impressora
                SingleWriter = false  // Múltiplos producers
            });
        });
    }
}

/// <summary>
/// Gerencia criação, deduplicação e atualização de jobs de impressão.
/// </summary>
public class JobService : IJobService
{
    private readonly ILogger<JobService> _logger;
    private readonly IJobRepository _jobRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly ProcessingOptions _options;

    public JobService(
        ILogger<JobService> logger,
        IJobRepository jobRepository,
        IAuditRepository auditRepository,
        IOptions<ProcessingOptions> options)
    {
        _logger = logger;
        _jobRepository = jobRepository;
        _auditRepository = auditRepository;
        _options = options.Value;
    }

    public async Task<PrintJob?> CreateJobAsync(
        string filePath,
        string fileHash,
        CancellationToken cancellationToken = default)
    {
        if (await IsDuplicateAsync(fileHash, cancellationToken))
        {
            return null;
        }

        var job = new PrintJob
        {
            Id = Guid.NewGuid(),
            OriginalFilePath = filePath,
            OriginalFileName = Path.GetFileName(filePath),
            OriginalHash = fileHash,
            Status = JobStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _jobRepository.AddAsync(job, cancellationToken);

        await _auditRepository.AddEventAsync(new AuditEvent
        {
            JobId = job.Id,
            EventType = AuditEventType.JobCreated,
            Description = $"Job criado para arquivo: {job.OriginalFileName}",
            Metadata = $"{{\"hash\":\"{fileHash}\"}}"
        }, cancellationToken);

        _logger.LogInformation("[Job {JobId}] Job criado. Arquivo: {File}", job.Id, job.OriginalFileName);
        return job;
    }

    public async Task<bool> IsDuplicateAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        // Sempre retorna falso para permitir reimpressão do mesmo arquivo
        return false;
    }

    public async Task UpdateStatusAsync(
        Guid jobId,
        JobStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} não encontrado para atualização de status.", jobId);
            return;
        }

        var previousStatus = job.Status;
        job.Status = status;
        job.ErrorMessage = errorMessage;

        if (status == JobStatus.Processing && !job.StartedAt.HasValue)
            job.StartedAt = DateTime.UtcNow;

        if (status is JobStatus.Archived or JobStatus.Failed or JobStatus.Cancelled)
            job.CompletedAt = DateTime.UtcNow;

        await _jobRepository.UpdateAsync(job, cancellationToken);

        _logger.LogInformation(
            "[Job {JobId}] Status: {Previous} → {New}{Error}",
            jobId, previousStatus, status,
            errorMessage != null ? $". Erro: {errorMessage}" : string.Empty);
    }

    public async Task<PrintJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        await _jobRepository.GetByIdAsync(jobId, cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> GetRecoverableJobsAsync(
        CancellationToken cancellationToken = default) =>
        await _jobRepository.GetActiveJobsAsync(cancellationToken);

    public async Task<PrintJob> ReprintJobAsync(
        Guid originalJobId,
        CancellationToken cancellationToken = default)
    {
        var original = await _jobRepository.GetByIdAsync(originalJobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job original {originalJobId} não encontrado.");

        if (string.IsNullOrEmpty(original.PdfPath) || !File.Exists(original.PdfPath))
            throw new InvalidOperationException($"PDF do job {originalJobId} não encontrado: {original.PdfPath}");

        var reprintJob = new PrintJob
        {
            Id = Guid.NewGuid(),
            OriginalFilePath = original.OriginalFilePath,
            OriginalFileName = original.OriginalFileName,
            OriginalHash = original.OriginalHash,
            PdfPath = original.PdfPath, // Reutiliza o PDF existente
            Marketplace = original.Marketplace,
            LabelSize = original.LabelSize,
            PrinterProfileId = original.PrinterProfileId,
            OrderReference = original.OrderReference,
            TrackingNumber = original.TrackingNumber,
            PackageNumber = original.PackageNumber,
            PackageCount = original.PackageCount,
            Status = JobStatus.PdfGenerated, // Pula diretamente para PdfGenerated
            IsReprint = true,
            OriginalJobId = originalJobId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _jobRepository.AddAsync(reprintJob, cancellationToken);

        await _auditRepository.AddEventAsync(new AuditEvent
        {
            JobId = reprintJob.Id,
            EventType = AuditEventType.ReprintRequested,
            Description = $"Reimpressão solicitada do job original {originalJobId}",
            Metadata = $"{{\"originalJobId\":\"{originalJobId}\"}}"
        }, cancellationToken);

        _logger.LogInformation(
            "[Job {JobId}] Reimpressão criada do job original {Original}.",
            reprintJob.Id, originalJobId);

        return reprintJob;
    }
}
