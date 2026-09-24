namespace LabelPrinter.Core.Configuration;

/// <summary>
/// Caminhos de diretórios usados pelo sistema.
/// Todos os valores são configuráveis via appsettings.json.
/// Nunca hardcode caminhos absolutos.
/// </summary>
public class PathsOptions
{
    public const string SectionName = "Paths";

    /// <summary>Pasta monitorada para novos arquivos. Default: %USERPROFILE%\Downloads</summary>
    public string Downloads { get; set; } = string.Empty;

    /// <summary>Pasta de trabalho temporário durante processamento.</summary>
    public string Processing { get; set; } = string.Empty;

    /// <summary>Pasta de arquivamento após processamento bem-sucedido.</summary>
    public string Archive { get; set; } = string.Empty;

    /// <summary>Pasta para jobs com falha (para diagnóstico).</summary>
    public string Failed { get; set; } = string.Empty;

    /// <summary>Pasta onde os PDFs gerados são salvos.</summary>
    public string Pdf { get; set; } = string.Empty;

    /// <summary>Pasta temporária para extração de ZIPs.</summary>
    public string Temp { get; set; } = string.Empty;

    /// <summary>Pasta de logs.</summary>
    public string Logs { get; set; } = string.Empty;

    /// <summary>Pasta do banco de dados SQLite.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Resolve os caminhos de Downloads de todos os usuários (útil para Windows Services).</summary>
    public List<string> GetResolvedDownloadsPaths()
    {
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(Downloads))
        {
            paths.Add(Environment.ExpandEnvironmentVariables(Downloads));
            return paths;
        }

        string usersDir = @"C:\Users";
        if (Directory.Exists(usersDir))
        {
            var userFolders = Directory.GetDirectories(usersDir);
            foreach (var userFolder in userFolders)
            {
                // Ignora pastas ocultas ou de sistema genéricas se desejar, mas checar a existência de Downloads já basta.
                string downloadDir = Path.Combine(userFolder, "Downloads");
                if (Directory.Exists(downloadDir))
                {
                    paths.Add(downloadDir);
                }
            }
        }

        if (paths.Count == 0)
        {
            paths.Add(Path.Combine(AppContext.BaseDirectory, "Downloads"));
        }

        return paths;
    }

    /// <summary>Resolve um caminho relativo ao BaseDirectory se não for absoluto.</summary>
    public static string ResolvePath(string path, string defaultRelative)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Path.Combine(AppContext.BaseDirectory, defaultRelative);

        if (Path.IsPathRooted(path))
            return Environment.ExpandEnvironmentVariables(path);

        return Path.Combine(AppContext.BaseDirectory, path);
    }
}

/// <summary>
/// Configurações do pipeline de processamento de jobs.
/// </summary>
public class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>Máximo de jobs processados simultaneamente.</summary>
    public int MaxConcurrentJobs { get; set; } = 4;

    /// <summary>Máximo de renders simultâneos (PDF).</summary>
    public int MaxConcurrentRendering { get; set; } = 2;

    /// <summary>Número máximo de tentativas de impressão por job.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Delay entre retries em segundos.</summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>Timeout máximo de processamento em segundos.</summary>
    public int ProcessingTimeoutSeconds { get; set; } = 120;

    /// <summary>Intervalo de polling de fallback para FileSystemWatcher (segundos).</summary>
    public int FolderPollingIntervalSeconds { get; set; } = 30;

    /// <summary>Extensões de arquivo aceitas para processamento.</summary>
    public string[] AcceptedExtensions { get; set; } =
        [".zip", ".7z", ".tar", ".gz", ".txt", ".csv", ".pdf", ".zpl", ".epl"];

    /// <summary>Dias de retenção de logs e arquivos arquivados.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Executar em modo simulado (não imprime).</summary>
    public bool SimulationMode { get; set; } = false;
}

/// <summary>
/// Limites de segurança para processamento de arquivos.
/// </summary>
public class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Tamanho máximo de arquivo de entrada em bytes. Default: 500 MB.</summary>
    public long MaxArchiveSizeBytes { get; set; } = 524_288_000; // 500 MB

    /// <summary>Tamanho máximo de conteúdo extraído em bytes. Default: 1 GB.</summary>
    public long MaxExtractedSizeBytes { get; set; } = 1_073_741_824; // 1 GB

    /// <summary>Número máximo de arquivos em um ZIP.</summary>
    public int MaxFilesInArchive { get; set; } = 1000;

    /// <summary>Número máximo de etiquetas por job.</summary>
    public int MaxLabelCount { get; set; } = 500;

    /// <summary>Tamanho máximo de texto de uma etiqueta em caracteres.</summary>
    public int MaxTextLength { get; set; } = 10_000;

    /// <summary>Extensões permitidas dentro de arquivos compactados.</summary>
    public string[] AllowedExtractedExtensions { get; set; } =
        [".txt", ".csv", ".pdf", ".zpl", ".epl", ".json", ".xml"];
}

/// <summary>
/// Configurações de estabilização de arquivo (aguardar download completar).
/// </summary>
public class FileStabilityOptions
{
    public const string SectionName = "FileStability";

    /// <summary>Número de verificações antes de considerar estável.</summary>
    public int CheckCount { get; set; } = 3;

    /// <summary>Intervalo entre verificações em milissegundos.</summary>
    public int CheckIntervalMs { get; set; } = 2000;

    /// <summary>Timeout total aguardando estabilidade em segundos.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Tamanho mínimo do arquivo para ser considerado válido em bytes.</summary>
    public long MinFileSizeBytes { get; set; } = 10;
}

/// <summary>
/// Configurações de etiquetas e templates.
/// </summary>
public class LabelOptions
{
    public const string SectionName = "Labels";

    public LabelTemplateConfig Standard100x150 { get; set; } = new()
    {
        Name = "Standard100x150",
        WidthMm = 100.0,
        HeightMm = 150.0,
        MarginMm = 2.0,
        DefaultDpi = 203
    };

    public LabelTemplateConfig Small50x30 { get; set; } = new()
    {
        Name = "Small50x30",
        WidthMm = 50.0,
        HeightMm = 30.0,
        MarginMm = 1.0,
        DefaultDpi = 203
    };
}

/// <summary>
/// Configuração de um template de etiqueta.
/// </summary>
public class LabelTemplateConfig
{
    public string Name { get; set; } = string.Empty;
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double MarginMm { get; set; } = 2.0;
    public int DefaultDpi { get; set; } = 203;
    public double MinQrCodeSizeMm { get; set; } = 20.0;
    public double MinBarcodeSizeMm { get; set; } = 40.0;
}

/// <summary>
/// Configuração de uma impressora via appsettings.json.
/// Nunca hardcode o nome — é lido do config e validado contra WMI no startup.
/// </summary>
public class PrinterProfileConfig
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Nome exato da impressora no Windows.
    /// Deixar vazio para usar a primeira impressora térmica encontrada.
    /// </summary>
    public string WindowsPrinterName { get; set; } = string.Empty;

    /// <summary>
    /// Nome do driver como critério de matching alternativo.
    /// Usado quando WindowsPrinterName não bate exatamente (máquinas diferentes).
    /// </summary>
    public string? DriverNamePattern { get; set; }

    public string Protocol { get; set; } = "GhostScript";
    public int Dpi { get; set; } = 203;
    public double DefaultLabelWidthMm { get; set; } = 100.0;
    public double DefaultLabelHeightMm { get; set; } = 150.0;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
    public int DefaultCopies { get; set; } = 1;
    public PrinterRoutingConfig[] RoutingRules { get; set; } = [];
}

/// <summary>
/// Regra de roteamento configurada no appsettings.json.
/// </summary>
public class PrinterRoutingConfig
{
    public string? Marketplace { get; set; }
    public string? LabelSize { get; set; }
    public int Priority { get; set; } = 100;
}

/// <summary>
/// Configuração de impressão geral.
/// </summary>
public class PrintingOptions
{
    public const string SectionName = "Printing";

    public PrinterProfileConfig[] Printers { get; set; } = [];

    /// <summary>Caminho do executável do GhostScript.</summary>
    public string GhostScriptPath { get; set; } = @"C:\Program Files\gs\gs10.05.1\bin\gswin64c.exe";

    /// <summary>Caminho do executável do SumatraPDF.</summary>
    public string SumatraPdfPath { get; set; } = @"C:\Program Files\SumatraPDF\SumatraPDF.exe";

    /// <summary>Timeout de impressão em segundos.</summary>
    public int PrintTimeoutSeconds { get; set; } = 60;

    /// <summary>Máximo de jobs simultâneos por impressora.</summary>
    public int MaxConcurrentPrintingPerPrinter { get; set; } = 1;
}

/// <summary>
/// Configurações do Windows Service.
/// </summary>
public class ServiceOptions
{
    public const string SectionName = "Service";

    public string ServiceName { get; set; } = "LabelPrinterService";
    public string DisplayName { get; set; } = "LabelPrinter - Impressão Automática de Etiquetas";
    public string Description { get; set; } = "Monitora a pasta Downloads e imprime automaticamente etiquetas térmicas.";

    /// <summary>Tempo máximo aguardando jobs ativos finalizarem no shutdown (segundos).</summary>
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;
}
