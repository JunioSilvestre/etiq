using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;

namespace LabelPrinter.Printing.Windows;

public class RawPrintService : IRawPrintService
{
    private readonly ILogger<RawPrintService> _logger;

    public RawPrintService(ILogger<RawPrintService> logger)
    {
        _logger = logger;
    }

    public async Task<PrintResult> PrintRawAsync(string filePath, string printerName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando impressão RAW (ZPL) para {Printer}. Arquivo: {File}", printerName, filePath);

        if (!File.Exists(filePath))
        {
            return PrintResult.Failed(PrintAttemptResult.UnknownError, "Arquivo bruto não encontrado.");
        }

        try
        {
            // Ler o arquivo ZPL. Usa UTF-8 como padrão.
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            
            bool success = RawPrinterHelper.SendBytesToPrinter(printerName, bytes, "ZPL_Label_" + Guid.NewGuid().ToString("N"));

            if (success)
            {
                _logger.LogInformation("Envio RAW concluído com sucesso para a impressora {Printer}.", printerName);
                return PrintResult.Succeeded();
            }
            else
            {
                var error = Marshal.GetLastWin32Error();
                return PrintResult.Failed(PrintAttemptResult.SpoolerError, $"Erro na API WinSpool. Código: {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar impressão RAW para {Printer}.", printerName);
            return PrintResult.Failed(PrintAttemptResult.UnknownError, ex.Message);
        }
    }
}

public static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static bool SendBytesToPrinter(string szPrinterName, byte[] bytes, string documentName)
    {
        IntPtr pUnmanagedBytes = new IntPtr(0);
        int nLength = bytes.Length;

        pUnmanagedBytes = Marshal.AllocCoTaskMem(nLength);
        Marshal.Copy(bytes, 0, pUnmanagedBytes, nLength);

        bool bSuccess = SendBytesToPrinter(szPrinterName, pUnmanagedBytes, nLength, documentName);
        
        Marshal.FreeCoTaskMem(pUnmanagedBytes);
        return bSuccess;
    }

    private static bool SendBytesToPrinter(string szPrinterName, IntPtr pBytes, int dwCount, string documentName)
    {
        int dwError = 0, dwWritten = 0;
        IntPtr hPrinter = new IntPtr(0);
        DOCINFOA di = new DOCINFOA();
        bool bSuccess = false;

        di.pDocName = documentName;
        di.pDataType = "RAW";

        if (OpenPrinter(szPrinterName.Normalize(), out hPrinter, IntPtr.Zero))
        {
            if (StartDocPrinter(hPrinter, 1, di))
            {
                if (StartPagePrinter(hPrinter))
                {
                    bSuccess = WritePrinter(hPrinter, pBytes, dwCount, out dwWritten);
                    EndPagePrinter(hPrinter);
                }
                EndDocPrinter(hPrinter);
            }
            ClosePrinter(hPrinter);
        }

        if (bSuccess == false)
        {
            dwError = Marshal.GetLastWin32Error();
        }
        return bSuccess;
    }
}
