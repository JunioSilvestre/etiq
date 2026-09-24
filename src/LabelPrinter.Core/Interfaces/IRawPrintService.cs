using LabelPrinter.Core.Models;
using System.Threading;
using System.Threading.Tasks;

namespace LabelPrinter.Core.Interfaces;

/// <summary>
/// Serviço para enviar dados brutos (Raw) diretamente ao spooler de impressão (ex: ZPL).
/// </summary>
public interface IRawPrintService
{
    /// <summary>
    /// Envia o conteúdo do arquivo bruto (TXT/ZPL) para a impressora.
    /// </summary>
    Task<PrintResult> PrintRawAsync(string filePath, string printerName, CancellationToken cancellationToken = default);
}
