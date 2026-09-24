namespace LabelPrinter.Core.Enums;

/// <summary>
/// Estados possíveis de um job de impressão ao longo do pipeline.
/// </summary>
public enum JobStatus
{
    /// <summary>Arquivo detectado pelo FileSystemWatcher ou polling.</summary>
    Detected = 0,

    /// <summary>Aguardando estabilização do arquivo (download em andamento).</summary>
    WaitingForStability = 1,

    /// <summary>Job criado e na fila para processamento.</summary>
    Queued = 2,

    /// <summary>Processamento em andamento (parsing, extração).</summary>
    Processing = 3,

    /// <summary>Etiqueta renderizada em memória.</summary>
    Rendered = 4,

    /// <summary>PDF gerado e salvo em disco.</summary>
    PdfGenerated = 5,

    /// <summary>Aguardando impressora ficar disponível.</summary>
    WaitingForPrinter = 6,

    /// <summary>Enviando para o spooler do Windows.</summary>
    Printing = 7,

    /// <summary>Aceito pelo spooler do Windows (pode ainda estar na fila física).</summary>
    AcceptedBySpooler = 8,

    /// <summary>Confirmado como impresso (quando possível verificar).</summary>
    Printed = 9,

    /// <summary>Job concluído e arquivado com sucesso.</summary>
    Archived = 10,

    /// <summary>Falha permanente — não será retentado automaticamente.</summary>
    Failed = 11,

    /// <summary>Aguardando retry após falha temporária.</summary>
    Retrying = 12,

    /// <summary>Cancelado manualmente ou por política.</summary>
    Cancelled = 13
}
