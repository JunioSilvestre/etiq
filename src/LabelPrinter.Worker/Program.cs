using LabelPrinter.Application.Services;
using LabelPrinter.Application.Workers;
using LabelPrinter.Worker;
using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Documents.Detectors;
using LabelPrinter.Documents.Extractors;
using LabelPrinter.Documents.Parsers;
using LabelPrinter.Infrastructure.Encoding;
using LabelPrinter.Infrastructure.FileSystem;
using LabelPrinter.Infrastructure.Hashing;
using LabelPrinter.Infrastructure.Printers;
using LabelPrinter.Labels.Renderers;
using LabelPrinter.Labels.Templates;
using LabelPrinter.Labels.Validators;
using LabelPrinter.Persistence;
using LabelPrinter.Printing.GhostScript;
using Microsoft.Extensions.Options;
using Serilog;

// ============================================================
// Configuração inicial do Serilog (antes do host builder)
// ============================================================
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Iniciando LabelPrinter...");

    var builderOptions = new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    };
    var builder = Host.CreateApplicationBuilder(builderOptions);

    // ============================================================
    // Windows Service support
    // Permite executar como console em dev e como Service em prod.
    // ============================================================
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "LabelPrinterService";
    });

    // ============================================================
    // Serilog configurado via appsettings.json
    // ============================================================
    builder.Services.AddSerilog((services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithMachineName();
    });

    // ============================================================
    // Options Pattern
    // ============================================================
    builder.Services.Configure<PathsOptions>(
        builder.Configuration.GetSection(PathsOptions.SectionName));
    builder.Services.Configure<ProcessingOptions>(
        builder.Configuration.GetSection(ProcessingOptions.SectionName));
    builder.Services.Configure<SecurityOptions>(
        builder.Configuration.GetSection(SecurityOptions.SectionName));
    builder.Services.Configure<FileStabilityOptions>(
        builder.Configuration.GetSection(FileStabilityOptions.SectionName));
    builder.Services.Configure<LabelOptions>(
        builder.Configuration.GetSection(LabelOptions.SectionName));
    builder.Services.Configure<PrintingOptions>(
        builder.Configuration.GetSection(PrintingOptions.SectionName));
    builder.Services.Configure<ServiceOptions>(
        builder.Configuration.GetSection(ServiceOptions.SectionName));

    // ============================================================
    // Persistência (SQLite + EF Core)
    // ============================================================
    var dbPath = ResolveDatabasePath(builder.Configuration);
    var connectionString = $"Data Source={dbPath};Cache=Shared;";

    builder.Services.AddPersistence(connectionString);

    // ============================================================
    // Infrastructure
    // ============================================================
    builder.Services.AddSingleton<IPrinterDiscoveryService, PrinterDiscoveryService>();
    builder.Services.AddSingleton<IPrinterHealthService, PrinterHealthService>();
    builder.Services.AddSingleton<IFileHashService, FileHashService>();
    builder.Services.AddSingleton<IFileStabilityChecker, FileStabilityChecker>();
    builder.Services.AddSingleton<IFormatDetector, FormatDetector>();
    builder.Services.AddSingleton<IEncodingDetector, EncodingDetectorService>();

    // ============================================================
    // Documents
    // ============================================================
    builder.Services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
    builder.Services.AddSingleton<IMarketplaceDetector, MarketplaceDetector>();

    // Parsers registrados em ordem de prioridade
    builder.Services.AddSingleton<TxtLabelParser>();
    builder.Services.AddSingleton<ILabelParser, ShopeeLabelParser>();
    builder.Services.AddSingleton<ILabelParser, AmazonLabelParser>();
    builder.Services.AddSingleton<ILabelParser, MercadoLivreLabelParser>();
    builder.Services.AddSingleton<ILabelParser>(sp => sp.GetRequiredService<TxtLabelParser>()); // fallback
    builder.Services.AddSingleton<LabelPrinter.Labels.Decoders.ZplImageDecoder>();

    // ============================================================
    // Labels (PDF Rendering)
    // ============================================================
    builder.Services.AddSingleton<PdfLabelRenderer>();
    builder.Services.AddSingleton<ILabelRenderer>(sp => sp.GetRequiredService<PdfLabelRenderer>());
    builder.Services.AddSingleton<IPdfGeneratorService>(sp => sp.GetRequiredService<PdfLabelRenderer>());
    builder.Services.AddSingleton<IPdfValidatorService, PdfValidatorService>();
    builder.Services.AddSingleton<BarcodeValidatorService>();
    builder.Services.AddSingleton<LabelTemplateResolver>();

    // ============================================================
    // Printing
    // ============================================================
    builder.Services.AddSingleton<IWindowsPrintService, LabelPrinter.Printing.Windows.SumatraPdfPrintService>();
    builder.Services.AddSingleton<IRawPrintService, LabelPrinter.Printing.Windows.RawPrintService>();

    // ============================================================
    // Application Services
    // ============================================================
    builder.Services.AddScoped<IPrinterProfileService, PrinterProfileService>();
    builder.Services.AddScoped<IJobService, JobService>();
    builder.Services.AddSingleton<IPrintQueueService, PrintQueueService>();
    builder.Services.AddScoped<IJobProcessingPipeline, JobProcessingPipeline>();

    // ============================================================
    // Background Workers
    // ============================================================
    builder.Services.AddHostedService<FileWatcherWorker>();
    builder.Services.AddHostedService<JobProcessingWorker>();
    builder.Services.AddHostedService<PrinterWorker>();

    // ============================================================
    // Startup / Initialization
    // ============================================================
    builder.Services.AddHostedService<StartupService>();

    var host = builder.Build();

    // Aplicar migrations do banco
    await LabelPrinter.Persistence.PersistenceServiceExtensions.EnsureDatabaseMigratedAsync(host.Services);

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Falha crítica na inicialização do LabelPrinter.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

static string ResolveDatabasePath(IConfiguration configuration)
{
    var configuredPath = configuration.GetSection("Paths:Database").Value;
    var baseDir = AppContext.BaseDirectory;

    string finalPath;
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        var resolved = Environment.ExpandEnvironmentVariables(configuredPath);
        // Se for um caminho relativo, combina com o BaseDirectory
        if (!Path.IsPathRooted(resolved))
        {
            resolved = Path.Combine(baseDir, resolved);
        }
        
        // Se não tiver extensão, assume que é um diretório
        if (string.IsNullOrEmpty(Path.GetExtension(resolved)))
        {
            Directory.CreateDirectory(resolved);
            finalPath = Path.Combine(resolved, "labelprinter.db");
        }
        else
        {
            var dir = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            finalPath = resolved;
        }
    }
    else
    {
        var defaultDir = Path.Combine(baseDir, "Data");
        Directory.CreateDirectory(defaultDir);
        finalPath = Path.Combine(defaultDir, "labelprinter.db");
    }

    return finalPath;
}
