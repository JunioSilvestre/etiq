using System.Threading.Channels;
using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Entities;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace LabelPrinter.Application.Workers;

/// <summary>
/// Worker que monitora a pasta Downloads por novos arquivos.
/// Combina FileSystemWatcher com polling periódico de fallback.
/// Nunca confia apenas em um único mecanismo de detecção.
/// </summary>
public class FileWatcherWorker : BackgroundService
{
    private readonly ILogger<FileWatcherWorker> _logger;
    private readonly IFileStabilityChecker _stabilityChecker;
    private readonly IFileHashService _hashService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _detectedFilesChannel;
    private readonly PathsOptions _paths;
    private readonly ProcessingOptions _processing;
    private readonly FileStabilityOptions _stability;
    private readonly List<FileSystemWatcher> _watchers = new();

    public FileWatcherWorker(
        ILogger<FileWatcherWorker> logger,
        IFileStabilityChecker stabilityChecker,
        IFileHashService hashService,
        IServiceScopeFactory scopeFactory,
        IOptions<PathsOptions> paths,
        IOptions<ProcessingOptions> pathsOpt,
        IOptions<ProcessingOptions> processing,
        IOptions<FileStabilityOptions> stability)
    {
        _logger = logger;
        _stabilityChecker = stabilityChecker;
        _hashService = hashService;
        _scopeFactory = scopeFactory;
        _paths = paths.Value;
        _processing = processing.Value;
        _stability = stability.Value;

        // Canal para coordenar detecção com estabilização
        _detectedFilesChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var downloadsPaths = _paths.GetResolvedDownloadsPaths();

        _logger.LogInformation("FileWatcherWorker iniciado. Monitorando {Count} pastas de Downloads.", downloadsPaths.Count);

        foreach (var downloadsPath in downloadsPaths)
        {
            if (!Directory.Exists(downloadsPath))
            {
                try
                {
                    Directory.CreateDirectory(downloadsPath);
                }
                catch (Exception)
                {
                    // Ignora erros de permissão se tentar criar na pasta de outro usuário
                }
            }

            if (Directory.Exists(downloadsPath))
            {
                // Iniciar FileSystemWatcher
                SetupFileSystemWatcher(downloadsPath, stoppingToken);
            }
        }

        // Iniciar polling de fallback em paralelo
        var pollingTask = RunPollingFallbackAsync(downloadsPaths, stoppingToken);

        // Processar arquivos detectados
        var processingTask = ProcessDetectedFilesAsync(stoppingToken);

        await Task.WhenAll(pollingTask, processingTask);

        _logger.LogInformation("FileWatcherWorker encerrado.");
    }

    private void SetupFileSystemWatcher(string path, CancellationToken stoppingToken)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            // Filtrar apenas extensões aceitas
            foreach (var ext in _processing.AcceptedExtensions)
                watcher.Filters.Add($"*{ext}");

            watcher.Created += async (_, args) =>
            {
                if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug("FileSystemWatcher: arquivo criado: {File}", args.FullPath);
                    await _detectedFilesChannel.Writer.WriteAsync(args.FullPath, stoppingToken);
                }
            };

            watcher.Changed += async (_, args) =>
            {
                if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug("FileSystemWatcher: arquivo alterado: {File}", args.FullPath);
                    await _detectedFilesChannel.Writer.WriteAsync(args.FullPath, stoppingToken);
                }
            };

            watcher.Error += (_, args) =>
            {
                _logger.LogError(args.GetException(), "Erro no FileSystemWatcher para {Path}.", path);
            };

            _watchers.Add(watcher);
            _logger.LogInformation("FileSystemWatcher ativado para: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao configurar FileSystemWatcher. Usando apenas polling.");
        }
    }

    private async Task RunPollingFallbackAsync(List<string> paths, CancellationToken stoppingToken)
    {
        var knownFiles = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var intervalMs = _processing.FolderPollingIntervalSeconds * 1000;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalMs, stoppingToken);

                foreach (var path in paths)
                {
                    if (!Directory.Exists(path)) continue;

                    foreach (var ext in _processing.AcceptedExtensions)
                    {
                        var files = Directory.GetFiles(path, $"*{ext}");
                        foreach (var file in files)
                        {
                            var lastWrite = File.GetLastWriteTimeUtc(file);
                            if (!knownFiles.TryGetValue(file, out var previousWrite) || lastWrite > previousWrite)
                            {
                                knownFiles[file] = lastWrite;
                                _logger.LogDebug("Polling: novo arquivo ou alteração detectada: {File}", file);
                                await _detectedFilesChannel.Writer.WriteAsync(file, stoppingToken);
                            }
                        }
                    }
                }

                // Limpar arquivos que não existem mais
                var missingFiles = knownFiles.Keys.Where(f => !File.Exists(f)).ToList();
                foreach (var missing in missingFiles)
                {
                    knownFiles.Remove(missing);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no polling de arquivos.");
            }
        }
    }

    private async Task ProcessDetectedFilesAsync(CancellationToken stoppingToken)
    {
        await foreach (var filePath in _detectedFilesChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessDetectedFileAsync(filePath, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar arquivo detectado: {File}", filePath);
            }
        }
    }

    private async Task ProcessDetectedFileAsync(string filePath, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Arquivo detectado: {File}", filePath);

        // 1. Aguardar estabilidade (download completo)
        var isStable = await _stabilityChecker.WaitForStabilityAsync(filePath, stoppingToken);
        if (!isStable)
        {
            _logger.LogWarning("Arquivo não estabilizou a tempo: {File}. Ignorando.", filePath);
            return;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Arquivo não existe mais: {File}", filePath);
            return;
        }

        // 2. Calcular hash para deduplicação
        string hash;
        try
        {
            hash = await _hashService.ComputeSha256Async(filePath, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao calcular hash do arquivo: {File}", filePath);
            return;
        }

        _logger.LogInformation("Arquivo estável: {File} | SHA-256: {Hash}", filePath, hash[..16] + "...");

        // 3. Criar job (já faz deduplicação internamente)
        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var job = await jobService.CreateJobAsync(filePath, hash, stoppingToken);
        if (job == null)
        {
            _logger.LogInformation("Arquivo ignorado (duplicata): {File}", filePath);
            return;
        }

        _logger.LogInformation("[Job {JobId}] Arquivo enfileirado para processamento.", job.Id);
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
        base.Dispose();
    }
}

/// <summary>
/// Worker que processa jobs da fila.
/// Respeita MaxConcurrentJobs e usa Channel para comunicação.
/// </summary>
public class JobProcessingWorker : BackgroundService
{
    private readonly ILogger<JobProcessingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPrintQueueService _printQueue;
    private readonly ProcessingOptions _options;

    public JobProcessingWorker(
        ILogger<JobProcessingWorker> logger,
        IServiceScopeFactory scopeFactory,
        IPrintQueueService printQueue,
        IOptions<ProcessingOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _printQueue = printQueue;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobProcessingWorker iniciado. MaxConcurrentJobs: {Max}", _options.MaxConcurrentJobs);

        // Recovery: reprocessar jobs interrompidos
        await RecoverInterruptedJobsAsync(stoppingToken);

        // Monitorar fila de jobs (polling no repositório)
        using var semaphore = new SemaphoreSlim(_options.MaxConcurrentJobs, _options.MaxConcurrentJobs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                var queuedJobs = await jobRepo.GetByStatusAsync(JobStatus.Queued, stoppingToken);

                foreach (var job in queuedJobs)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    await semaphore.WaitAsync(stoppingToken);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessJobAsync(job.Id, stoppingToken);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, stoppingToken);
                }

                await Task.Delay(2000, stoppingToken); // Check queue every 2s
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no JobProcessingWorker.");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("JobProcessingWorker encerrado.");
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IJobProcessingPipeline>();
        var printerProfileService = scope.ServiceProvider.GetRequiredService<IPrinterProfileService>();
        var jobRepository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var auditRepository = scope.ServiceProvider.GetRequiredService<IAuditRepository>();

        var job = await jobRepository.GetByIdAsync(jobId, stoppingToken);
        if (job == null) return;

        _logger.LogInformation("[Job {JobId}] Iniciando processamento.", job.Id);

        try
        {
            var result = await pipeline.ProcessAsync(job, stoppingToken);

            if (!result.Success)
            {
                job.AttemptCount++;
                job.ErrorMessage = result.ErrorMessage;

                if (result.IsPermanentError || job.AttemptCount >= _options.MaxRetries)
                {
                    await jobService.UpdateStatusAsync(job.Id, JobStatus.Failed, result.ErrorMessage, stoppingToken);
                    await ArchiveToFailedAsync(job, stoppingToken);
                }
                else
                {
                    await jobService.UpdateStatusAsync(job.Id, JobStatus.Retrying, result.ErrorMessage, stoppingToken);
                    _logger.LogWarning("[Job {JobId}] Retentativa {Count}/{Max} agendada.",
                        job.Id, job.AttemptCount, _options.MaxRetries);
                }
                return;
            }

            // Selecionar impressora e enfileirar para impressão
            var printerProfile = await printerProfileService.SelectPrinterForJobAsync(job, stoppingToken);
            if (printerProfile == null)
            {
                _logger.LogWarning("[Job {JobId}] Nenhuma impressora disponível. Status: WaitingForPrinter.", job.Id);
                await jobService.UpdateStatusAsync(job.Id, JobStatus.WaitingForPrinter, cancellationToken: stoppingToken);
                return;
            }

            job.PrinterProfileId = printerProfile.Id;
            job.PrinterProfile = printerProfile;
            await jobRepository.UpdateAsync(job, stoppingToken);

            await _printQueue.EnqueueAsync(job, stoppingToken);

            await auditRepository.AddEventAsync(new AuditEvent
            {
                JobId = job.Id,
                EventType = AuditEventType.PrintQueued,
                Description = $"Job enfileirado para impressora: {printerProfile.WindowsPrinterName}"
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Job {JobId}] Erro crítico no processamento.", job.Id);
            await jobService.UpdateStatusAsync(job.Id, JobStatus.Retrying, ex.Message, stoppingToken);
        }
    }

    private async Task RecoverInterruptedJobsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var auditRepository = scope.ServiceProvider.GetRequiredService<IAuditRepository>();

        _logger.LogInformation("Verificando jobs interrompidos para recovery...");

        var recoverableJobs = await jobService.GetRecoverableJobsAsync(stoppingToken);

        if (!recoverableJobs.Any())
        {
            _logger.LogInformation("Nenhum job interrompido encontrado.");
            return;
        }

        _logger.LogWarning("Recovery: {Count} job(s) interrompido(s) encontrado(s).", recoverableJobs.Count);

        foreach (var job in recoverableJobs)
        {
            // Jobs que tinham PDF gerado voltam para fila de impressão
            if (job.Status is JobStatus.PdfGenerated or JobStatus.WaitingForPrinter)
            {
                _logger.LogInformation("[Job {JobId}] Recovery: voltando para WaitingForPrinter.", job.Id);
                await jobService.UpdateStatusAsync(job.Id, JobStatus.Queued, cancellationToken: stoppingToken);
            }
            // Jobs em processamento voltam para Queued
            else if (job.Status is JobStatus.Processing or JobStatus.Rendered)
            {
                _logger.LogInformation("[Job {JobId}] Recovery: voltando para Queued.", job.Id);
                await jobService.UpdateStatusAsync(job.Id, JobStatus.Queued, cancellationToken: stoppingToken);
            }

            await auditRepository.AddEventAsync(new AuditEvent
            {
                JobId = job.Id,
                EventType = AuditEventType.RecoveryCompleted,
                Description = $"Job recuperado após reinicialização. Status anterior: {job.Status}"
            }, stoppingToken);
        }
    }

    private async Task ArchiveToFailedAsync(PrintJob job, CancellationToken stoppingToken)
    {
        // Mover arquivo original para pasta Failed
        try
        {
            if (File.Exists(job.OriginalFilePath))
            {
                var failedDir = Path.Combine(
                    PathsOptions.ResolvePath(string.Empty, "Failed"),
                    DateTime.UtcNow.ToString("yyyy/MM/dd"),
                    job.Id.ToString());

                Directory.CreateDirectory(failedDir);
                var destPath = Path.Combine(failedDir, job.OriginalFileName);
                File.Copy(job.OriginalFilePath, destPath, overwrite: true);

                _logger.LogInformation("[Job {JobId}] Arquivo movido para Failed: {Path}", job.Id, destPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Job {JobId}] Erro ao arquivar em Failed.", job.Id);
        }
    }
}

/// <summary>
/// Worker de impressão para uma impressora específica.
/// Processa a fila FIFO de jobs para a impressora.
/// </summary>
public class PrinterWorker : BackgroundService
{
    private readonly ILogger<PrinterWorker> _logger;
    private readonly IPrintQueueService _printQueue;
    private readonly IWindowsPrintService _printService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPrinterHealthService _printerHealth;
    private readonly ProcessingOptions _options;

    public PrinterWorker(
        ILogger<PrinterWorker> logger,
        IServiceScopeFactory scopeFactory,
        IPrintQueueService printQueue,
        IWindowsPrintService printService,
        IPrinterHealthService printerHealth,
        IOptions<ProcessingOptions> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _printQueue = printQueue;
        _printService = printService;
        _printerHealth = printerHealth;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PrinterWorker Manager iniciado.");

        List<string> activePrinters = new();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var profileService = scope.ServiceProvider.GetRequiredService<IPrinterProfileService>();
            var profiles = await profileService.GetEnabledProfilesAsync(stoppingToken);
            
            activePrinters = profiles.Where(p => !string.IsNullOrEmpty(p.WindowsPrinterName))
                                         .Select(p => p.WindowsPrinterName).Distinct().ToList();

            if (activePrinters.Any())
                break;

            _logger.LogInformation("Nenhuma impressora ativa encontrada ainda. Aguardando inicializacao...");
            await Task.Delay(2000, stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested) return;

        _logger.LogInformation("Iniciando {Count} workers de impressão.", activePrinters.Count);

        var tasks = new List<Task>();
        foreach (var printerName in activePrinters)
        {
            tasks.Add(Task.Run(() => ProcessPrinterQueueAsync(printerName, stoppingToken), stoppingToken));
        }

        await Task.WhenAll(tasks);
        _logger.LogInformation("PrinterWorker Manager encerrado.");
    }

    private async Task ProcessPrinterQueueAsync(string printerName, CancellationToken stoppingToken)
    {
        _logger.LogInformation("PrinterWorker iniciado para impressora: {Printer}", printerName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _printQueue.DequeueAsync(printerName, stoppingToken);
                if (job == null) continue;

                await PrintJobAsync(job, printerName, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no PrinterWorker para {Printer}.", printerName);
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("PrinterWorker encerrado para impressora: {Printer}", printerName);
    }

    private async Task PrintJobAsync(PrintJob jobQueueRef, string printerName, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var auditRepository = scope.ServiceProvider.GetRequiredService<IAuditRepository>();
        var jobRepository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var profileService = scope.ServiceProvider.GetRequiredService<IPrinterProfileService>();

        // Re-fetch the job in the current scope to avoid EF tracking conflicts
        var job = await jobRepository.GetByIdAsync(jobQueueRef.Id, stoppingToken);
        if (job == null) return;

        _logger.LogInformation("[Job {JobId}] Processando impressão na impressora: {Printer}",
            job.Id, printerName);

        // Verificar impressora antes de tentar
        var isOnline = await _printerHealth.IsPrinterOnlineAsync(printerName, stoppingToken);
        if (!isOnline)
        {
            _logger.LogWarning("[Job {JobId}] Impressora {Printer} offline. Aguardando...", job.Id, printerName);
            await jobService.UpdateStatusAsync(job.Id, JobStatus.WaitingForPrinter, cancellationToken: stoppingToken);

            await auditRepository.AddEventAsync(new AuditEvent
            {
                JobId = job.Id,
                EventType = AuditEventType.PrinterOffline,
                Description = $"Impressora offline: {printerName}"
            }, stoppingToken);

            // Re-enfileirar após delay
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await _printQueue.EnqueueAsync(job, stoppingToken);
            return;
        }

        // Validar PDF ainda existe
        if (string.IsNullOrEmpty(job.PdfPath) || !File.Exists(job.PdfPath))
        {
            _logger.LogError("[Job {JobId}] PDF não encontrado: {Path}", job.Id, job.PdfPath);
            await jobService.UpdateStatusAsync(job.Id, JobStatus.Failed,
                $"PDF não encontrado: {job.PdfPath}", stoppingToken);
            return;
        }

        // Registrar tentativa
        var attempt = new PrintAttempt
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            AttemptNumber = job.AttemptCount + 1,
            StartedAt = DateTime.UtcNow,
            PrinterName = printerName
        };

        await auditRepository.AddPrintAttemptAsync(attempt, stoppingToken);
        await jobService.UpdateStatusAsync(job.Id, JobStatus.Printing, cancellationToken: stoppingToken);

        // Imprimir
        var copies = job.PrinterProfile?.DefaultCopies ?? 1;
        PrintResult printResult;

        if (job.IsRawData)
        {
            var rawPrintService = scope.ServiceProvider.GetRequiredService<IRawPrintService>();
            printResult = await rawPrintService.PrintRawAsync(job.PdfPath, printerName, stoppingToken);
        }
        else
        {
            printResult = await _printService.PrintAsync(job.PdfPath, printerName, copies, stoppingToken);
        }

        attempt.CompletedAt = DateTime.UtcNow;
        attempt.Result = printResult.AttemptResult;
        attempt.ErrorMessage = printResult.ErrorMessage;
        attempt.SpoolerJobId = printResult.SpoolerJobId;

        job.AttemptCount++;

        if (printResult.Success)
        {
            await jobService.UpdateStatusAsync(job.Id, JobStatus.AcceptedBySpooler, cancellationToken: stoppingToken);

            await auditRepository.AddEventAsync(new AuditEvent
            {
                JobId = job.Id,
                EventType = AuditEventType.PrintSucceeded,
                Description = $"Job aceito pelo spooler da impressora: {printerName}",
                Metadata = $"{{\"spoolerJobId\":{attempt.SpoolerJobId}}}"
            }, stoppingToken);

            // Arquivar arquivo original
            await ArchiveOriginalFileAsync(job, auditRepository, stoppingToken);
            await jobService.UpdateStatusAsync(job.Id, JobStatus.Archived, cancellationToken: stoppingToken);

            _logger.LogInformation("[Job {JobId}] Impresso com sucesso em: {Printer}", job.Id, printerName);
        }
        else
        {
            _logger.LogError("[Job {JobId}] Falha na impressão: {Error}", job.Id, printResult.ErrorMessage);

            var maxRetries = job.PrinterProfile?.MaxRetries ?? _options.MaxRetries;

            if (printResult.AttemptResult == PrintAttemptResult.PrinterOffline)
            {
                await jobService.UpdateStatusAsync(job.Id, JobStatus.WaitingForPrinter,
                    printResult.ErrorMessage, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(job.PrinterProfile?.RetryDelaySeconds ?? _options.RetryDelaySeconds), stoppingToken);
                await _printQueue.EnqueueAsync(job, stoppingToken);
            }
            else if (job.AttemptCount < maxRetries)
            {
                await jobService.UpdateStatusAsync(job.Id, JobStatus.Retrying,
                    printResult.ErrorMessage, stoppingToken);
                var retryDelay = job.PrinterProfile?.RetryDelaySeconds ?? _options.RetryDelaySeconds;
                await Task.Delay(TimeSpan.FromSeconds(retryDelay), stoppingToken);
                await _printQueue.EnqueueAsync(job, stoppingToken);
            }
            else
            {
                await jobService.UpdateStatusAsync(job.Id, JobStatus.Failed,
                    $"Máximo de tentativas atingido: {printResult.ErrorMessage}", stoppingToken);
            }
        }

        await jobRepository.UpdateAsync(job, stoppingToken);
    }

    private async Task ArchiveOriginalFileAsync(PrintJob job, IAuditRepository auditRepository, CancellationToken stoppingToken)
    {
        try
        {
            if (!File.Exists(job.OriginalFilePath)) return;

            var now = DateTime.UtcNow;
            var archiveDir = Path.Combine(
                PathsOptions.ResolvePath(string.Empty, "Archive"),
                now.ToString("yyyy/MM/dd"),
                job.Id.ToString());

            Directory.CreateDirectory(archiveDir);
            var destPath = Path.Combine(archiveDir, job.OriginalFileName);
            File.Copy(job.OriginalFilePath, destPath, overwrite: true);
            File.Delete(job.OriginalFilePath);

            job.ArchivePath = archiveDir;

            _logger.LogInformation("[Job {JobId}] Arquivo arquivado: {Path}", job.Id, destPath);

            await auditRepository.AddEventAsync(new AuditEvent
            {
                JobId = job.Id,
                EventType = AuditEventType.JobArchived,
                Description = $"Arquivo original arquivado: {destPath}"
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Job {JobId}] Erro ao arquivar arquivo original.", job.Id);
        }
    }
}
