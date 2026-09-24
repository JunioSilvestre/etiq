using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Entities;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Exceptions;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using LabelPrinter.Labels.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace LabelPrinter.Application.Services;

/// <summary>
/// Pipeline de processamento de um job de impressão.
/// Orquestra todas as etapas: extração → parsing → normalização → PDF → validação.
/// Cada etapa é isolada — erros em uma etapa não afetam outras.
/// </summary>
public class JobProcessingPipeline : IJobProcessingPipeline
{
    private readonly ILogger<JobProcessingPipeline> _logger;
    private readonly IFormatDetector _formatDetector;
    private readonly IArchiveExtractor _archiveExtractor;
    private readonly IEncodingDetector _encodingDetector;
    private readonly IMarketplaceDetector _marketplaceDetector;
    private readonly IEnumerable<ILabelParser> _parsers;
    private readonly IPdfGeneratorService _pdfGenerator;
    private readonly IPdfValidatorService _pdfValidator;
    private readonly LabelTemplateResolver _templateResolver;
    private readonly IJobRepository _jobRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly LabelPrinter.Labels.Decoders.ZplImageDecoder _zplDecoder;
    private readonly PathsOptions _paths;
    private readonly ProcessingOptions _processing;

    public JobProcessingPipeline(
        ILogger<JobProcessingPipeline> logger,
        IFormatDetector formatDetector,
        IArchiveExtractor archiveExtractor,
        IEncodingDetector encodingDetector,
        IMarketplaceDetector marketplaceDetector,
        IEnumerable<ILabelParser> parsers,
        IPdfGeneratorService pdfGenerator,
        IPdfValidatorService pdfValidator,
        LabelTemplateResolver templateResolver,
        IJobRepository jobRepository,
        IAuditRepository auditRepository,
        LabelPrinter.Labels.Decoders.ZplImageDecoder zplDecoder,
        IOptions<PathsOptions> paths,
        IOptions<ProcessingOptions> processing)
    {
        _logger = logger;
        _formatDetector = formatDetector;
        _archiveExtractor = archiveExtractor;
        _encodingDetector = encodingDetector;
        _marketplaceDetector = marketplaceDetector;
        _parsers = parsers;
        _pdfGenerator = pdfGenerator;
        _pdfValidator = pdfValidator;
        _templateResolver = templateResolver;
        _jobRepository = jobRepository;
        _auditRepository = auditRepository;
        _zplDecoder = zplDecoder;
        _paths = paths.Value;
        _processing = processing.Value;
    }

    public async Task<ProcessingResult> ProcessAsync(
        PrintJob job,
        CancellationToken cancellationToken = default)
    {
        var jobId = job.Id;
        _logger.LogInformation("[Job {JobId}] Iniciando processamento. Arquivo: {File}",
            jobId, job.OriginalFileName);

        job.StartedAt = DateTime.UtcNow;
        job.Status = JobStatus.Processing;
        await _jobRepository.UpdateAsync(job, cancellationToken);

        string? extractedDirectory = null;

        try
        {
            // === ETAPA 1: Detectar formato ===
            var format = _formatDetector.DetectFormat(job.OriginalFilePath);
            _logger.LogInformation("[Job {JobId}] Formato detectado: {Format}", jobId, format);

            // === ETAPA 2: Extrair se for arquivo compactado ===
            string? labelFilePath = null;

            if (_formatDetector.IsArchive(format))
            {
                var tempDir = Path.Combine(
                    PathsOptions.ResolvePath(_paths.Temp, "Temp"),
                    jobId.ToString());

                var extractResult = await _archiveExtractor.ExtractAsync(
                    job.OriginalFilePath, tempDir, cancellationToken);

                if (!extractResult.Success)
                    return ProcessingResult.PermanentFailure($"Falha na extração: {extractResult.ErrorMessage}");

                extractedDirectory = tempDir;
                job.ExtractedPath = tempDir;

                await _auditRepository.AddEventAsync(new AuditEvent
                {
                    JobId = jobId,
                    EventType = AuditEventType.ArchiveExtracted,
                    Description = $"Arquivo extraído: {extractResult.ExtractedFiles.Count} arquivo(s)",
                    Metadata = JsonConvert.SerializeObject(new { files = extractResult.ExtractedFiles.Count, bytes = extractResult.TotalExtractedBytes })
                }, cancellationToken);

                // Localizar arquivo de etiqueta dentro do ZIP
                labelFilePath = FindLabelFile(extractResult.ExtractedFiles);
                if (labelFilePath == null)
                    return ProcessingResult.PermanentFailure("Nenhum arquivo de etiqueta encontrado no arquivo compactado.");
            }
            else if (_formatDetector.IsLabelFormat(format))
            {
                labelFilePath = job.OriginalFilePath;
            }
            else
            {
                return ProcessingResult.PermanentFailure($"Formato não suportado: {format}");
            }

            // === BYPASS: Se for PDF, pula direto para a impressão ===
            if (labelFilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[Job {JobId}] PDF direto detectado. Pulando parser.", jobId);
                
                var pdfDir = BuildPdfPath(job);
                Directory.CreateDirectory(pdfDir);
                var finalPdfPath = Path.Combine(pdfDir, "label.pdf");
                
                File.Copy(labelFilePath, finalPdfPath, overwrite: true);
                
                job.PdfPath = finalPdfPath;
                job.IsRawData = false;
                job.Marketplace = Marketplace.Unknown;
                job.Status = JobStatus.PdfGenerated;
                await _jobRepository.UpdateAsync(job, cancellationToken);
                
                await _auditRepository.AddEventAsync(new AuditEvent
                {
                    JobId = jobId,
                    EventType = AuditEventType.PdfGenerated,
                    Description = $"PDF direto copiado: {finalPdfPath}",
                    Metadata = JsonConvert.SerializeObject(new { path = finalPdfPath, directPdf = true })
                }, cancellationToken);
                
                var dummyDocument = new LabelDocument { JobId = jobId, Marketplace = Marketplace.Unknown, IsRawZpl = false };
                return ProcessingResult.Succeeded(dummyDocument, finalPdfPath);
            }

            // === ETAPA 3: Detectar encoding ===
            var encoding = _encodingDetector.DetectEncoding(labelFilePath);
            job.DetectedEncoding = encoding.WebName;

            await _auditRepository.AddEventAsync(new AuditEvent
            {
                JobId = jobId,
                EventType = AuditEventType.EncodingDetected,
                Description = $"Encoding detectado: {encoding.WebName}"
            }, cancellationToken);

            _logger.LogInformation("[Job {JobId}] Encoding: {Encoding}", jobId, encoding.WebName);

            // === ETAPA 4: Parse ===
            LabelDocument? document = null;

            // Tentar parsers específicos primeiro (por prioridade: Shopee > Amazon > MercadoLivre > genérico)
            var orderedParsers = _parsers.OrderBy(p => p switch
            {
                _ when p.ParserName.Contains("Shopee") => 1,
                _ when p.ParserName.Contains("Amazon") => 2,
                _ when p.ParserName.Contains("MercadoLivre") => 3,
                _ => 99
            });

            string? previewContent = null;
            try
            {
                previewContent = await File.ReadAllTextAsync(labelFilePath, encoding, cancellationToken);
            }
            catch { /* preview falhou, continuar sem ele */ }

            foreach (var parser in orderedParsers)
            {
                if (parser.CanParse(labelFilePath, previewContent))
                {
                    _logger.LogInformation("[Job {JobId}] Usando parser: {Parser}", jobId, parser.ParserName);
                    document = await parser.ParseAsync(labelFilePath, encoding, cancellationToken);

                    if (document != null) break;
                }
            }

            if (document == null)
                return ProcessingResult.PermanentFailure("Nenhum parser conseguiu interpretar o arquivo.");

            document.JobId = jobId;

            await _auditRepository.AddEventAsync(new AuditEvent
            {
                JobId = jobId,
                EventType = AuditEventType.LabelParsed,
                Description = $"Etiqueta parsed. Marketplace: {document.Marketplace}, Tracking: {document.TrackingNumber}",
                Metadata = JsonConvert.SerializeObject(new
                {
                    marketplace = document.Marketplace.ToString(),
                    tracking = document.TrackingNumber,
                    recipient = document.RecipientName,
                    labelSize = document.LabelSize.ToString()
                })
            }, cancellationToken);

            // === ETAPA 5: Atualizar job com dados do documento ===
            job.Marketplace = document.Marketplace;
            job.LabelSize = document.LabelSize;
            job.OrderReference = document.OrderReference;
            job.TrackingNumber = document.TrackingNumber;
            job.PackageNumber = document.PackageNumber;
            job.PackageCount = document.PackageCount;
            job.Status = JobStatus.Rendered;
            await _jobRepository.UpdateAsync(job, cancellationToken);

            // === ETAPA 6: Resolver template e gerar PDF (ou Raw ZPL) ===
            var pdfDirectory = BuildPdfPath(job);
            Directory.CreateDirectory(pdfDirectory);
            var finalPath = Path.Combine(pdfDirectory, document.IsRawZpl ? "label.zpl" : "label.pdf");

            if (document.IsRawZpl)
            {
                var zplContent = await File.ReadAllTextAsync(labelFilePath, encoding, cancellationToken);
                if (_zplDecoder.IsZplGraphic(zplContent))
                {
                    _logger.LogInformation("[Job {JobId}] ZPL gráfico Z64 detectado. Convertendo para PDF offline...", jobId);
                    finalPath = Path.Combine(pdfDirectory, "label.pdf");
                    
                    // Converte ZPL Z64 para PDF
                    finalPath = await _zplDecoder.ConvertZplToPdfAsync(zplContent, finalPath, cancellationToken);
                    
                    job.PdfPath = finalPath;
                    job.IsRawData = false; // Não é mais RAW, agora é PDF!
                    job.Status = JobStatus.PdfGenerated;
                    await _jobRepository.UpdateAsync(job, cancellationToken);

                    await _auditRepository.AddEventAsync(new AuditEvent
                    {
                        JobId = jobId,
                        EventType = AuditEventType.PdfGenerated,
                        Description = $"PDF gerado a partir de imagem ZPL (Z64): {finalPath}",
                        Metadata = JsonConvert.SerializeObject(new { path = finalPath, zplConverted = true })
                    }, cancellationToken);
                }
                else
                {
                    // Se for um ZPL sem Z64 que não conseguimos converter offline, ainda passamos para RAW (ou rejeitamos)
                    File.Copy(labelFilePath, finalPath, overwrite: true);
                    job.PdfPath = finalPath;
                    job.IsRawData = true;
                    job.Status = JobStatus.PdfGenerated;
                    await _jobRepository.UpdateAsync(job, cancellationToken);

                    await _auditRepository.AddEventAsync(new AuditEvent
                    {
                        JobId = jobId,
                        EventType = AuditEventType.PdfGenerated,
                        Description = $"Arquivo bruto ZPL copiado: {finalPath}",
                        Metadata = JsonConvert.SerializeObject(new { path = finalPath, isRaw = true })
                    }, cancellationToken);

                    _logger.LogInformation("[Job {JobId}] ZPL bruto preparado: {Path}", jobId, finalPath);
                }
            }
            else
            {
                var template = _templateResolver.Resolve(document.LabelSize);
                finalPath = await _pdfGenerator.GeneratePdfAsync(document, template, finalPath, cancellationToken);

                job.PdfPath = finalPath;
                job.Status = JobStatus.PdfGenerated;
                await _jobRepository.UpdateAsync(job, cancellationToken);

                await _auditRepository.AddEventAsync(new AuditEvent
                {
                    JobId = jobId,
                    EventType = AuditEventType.PdfGenerated,
                    Description = $"PDF gerado: {finalPath}",
                    Metadata = JsonConvert.SerializeObject(new { path = finalPath, template = template.TemplateName })
                }, cancellationToken);

                _logger.LogInformation("[Job {JobId}] PDF gerado: {Path}", jobId, finalPath);

                // === ETAPA 7: Validar PDF ===
                var validationResult = await _pdfValidator.ValidateAsync(finalPath, template, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var errors = string.Join("; ", validationResult.Errors);
                    _logger.LogError("[Job {JobId}] PDF inválido: {Errors}", jobId, errors);
                    return ProcessingResult.PermanentFailure($"PDF gerado é inválido: {errors}");
                }
            }

            // === ETAPA 8: Salvar metadata ===
            await SaveJobMetadataAsync(job, document, pdfDirectory, cancellationToken);

            // === ETAPA EXTRA: Copiar para Arquivos-PDF ===
            try
            {
                var projectRoot = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName;
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    var exportDir = Path.Combine(projectRoot, "src", "Arquivos-PDF");
                    Directory.CreateDirectory(exportDir);
                    var exportPath = Path.Combine(exportDir, $"{job.TrackingNumber ?? job.OrderReference ?? jobId.ToString()}.pdf");
                    File.Copy(finalPath, exportPath, overwrite: true);
                    _logger.LogInformation("[Job {JobId}] PDF copiado para pasta de exportação: {Path}", jobId, exportPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Job {JobId}] Falha ao copiar PDF para Arquivos-PDF.", jobId);
            }

            // === ETAPA EXTRA: Limpeza Automática (Excluir arquivo original) ===
            try
            {
                if (File.Exists(job.OriginalFilePath))
                {
                    File.Delete(job.OriginalFilePath);
                    _logger.LogInformation("[Job {JobId}] Arquivo original excluído com sucesso: {Path}", jobId, job.OriginalFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Job {JobId}] Falha ao excluir arquivo original: {Path}", jobId, job.OriginalFilePath);
            }

            _logger.LogInformation(
                "[Job {JobId}] Processamento concluído. Marketplace: {Marketplace}, FinalPath: {Path}",
                jobId, document.Marketplace, finalPath);

            return ProcessingResult.Succeeded(document, finalPath);
        }
        catch (PermanentProcessingException ex)
        {
            _logger.LogError(ex, "[Job {JobId}] Erro permanente: {Message}", jobId, ex.Message);
            return ProcessingResult.PermanentFailure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Job {JobId}] Erro inesperado no processamento.", jobId);
            return ProcessingResult.TemporaryFailure($"Erro inesperado: {ex.Message}");
        }
        finally
        {
            // Limpar diretório temporário de extração
            if (extractedDirectory != null)
            {
                try
                {
                    if (Directory.Exists(extractedDirectory))
                        Directory.Delete(extractedDirectory, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Job {JobId}] Erro ao limpar diretório temporário: {Dir}",
                        jobId, extractedDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Localiza o arquivo de etiqueta principal dentro dos arquivos extraídos.
    /// Prioriza: ZPL > TXT > CSV.
    /// </summary>
    private static string? FindLabelFile(IList<string> files)
    {
        var priorityOrder = new[] { ".zpl", ".epl", ".txt", ".csv", ".pdf" };

        foreach (var ext in priorityOrder)
        {
            var match = files.FirstOrDefault(f =>
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return files.FirstOrDefault();
    }

    /// <summary>
    /// Constrói o caminho do diretório do PDF seguindo a estrutura Data/Ano/Mês/Dia/JobId/.
    /// </summary>
    private string BuildPdfPath(PrintJob job)
    {
        var now = DateTime.UtcNow;
        var basePath = PathsOptions.ResolvePath(_paths.Pdf, "PDFs");
        return Path.Combine(
            basePath,
            now.Year.ToString("D4"),
            now.Month.ToString("D2"),
            now.Day.ToString("D2"),
            job.Id.ToString());
    }

    private async Task SaveJobMetadataAsync(
        PrintJob job,
        LabelDocument document,
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = new
            {
                jobId = job.Id,
                createdAt = job.CreatedAt,
                originalFile = job.OriginalFileName,
                hash = job.OriginalHash,
                marketplace = document.Marketplace.ToString(),
                tracking = document.TrackingNumber,
                recipient = document.RecipientName,
                labelSize = document.LabelSize.ToString(),
                encoding = document.DetectedEncoding
            };

            var metadataJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            var metadataPath = Path.Combine(directory, "metadata.json");
            await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Job {JobId}] Erro ao salvar metadata.", job.Id);
        }
    }
}
