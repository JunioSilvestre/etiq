using LabelPrinter.Core.Interfaces;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace LabelPrinter.Labels.Validators;

/// <summary>
/// Valida PDF gerado antes de enviar para impressão.
/// </summary>
public class PdfValidatorService : IPdfValidatorService
{
    private readonly ILogger<PdfValidatorService> _logger;

    public PdfValidatorService(ILogger<PdfValidatorService> logger)
    {
        _logger = logger;
    }

    public async Task<PdfValidationResult> ValidateAsync(
        string pdfPath,
        ILabelTemplate template,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[PdfValidator] Validando: {Path}", pdfPath);

        if (!File.Exists(pdfPath))
            return PdfValidationResult.Invalid($"PDF não encontrado: {pdfPath}");

        var fileInfo = new FileInfo(pdfPath);
        if (fileInfo.Length < 100)
            return PdfValidationResult.Invalid("PDF muito pequeno, possível arquivo corrompido.");

        // Verificar magic bytes do PDF (%PDF)
        var header = new byte[5];
        await using (var stream = File.OpenRead(pdfPath))
        {
            await stream.ReadAsync(header.AsMemory(0, 5), cancellationToken);
        }

        if (!IsPdfMagicBytes(header))
            return PdfValidationResult.Invalid("Arquivo não é um PDF válido (magic bytes incorretos).");

        try
        {
            // Verificar integridade básica
            await Task.Run(() =>
            {
                using var fs = File.OpenRead(pdfPath);
                var buffer = new byte[Math.Min(1024, fileInfo.Length)];
                fs.Read(buffer, 0, buffer.Length);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            return PdfValidationResult.Invalid($"Erro ao abrir PDF: {ex.Message}");
        }

        _logger.LogInformation(
            "[PdfValidator] PDF válido: {Path} ({Size} bytes). Template: {Width}x{Height}mm",
            pdfPath, fileInfo.Length, template.WidthMm, template.HeightMm);

        return PdfValidationResult.Valid(1, template.WidthMm, template.HeightMm);
    }

    private static bool IsPdfMagicBytes(byte[] header) =>
        header.Length >= 4 &&
        header[0] == 0x25 && header[1] == 0x50 &&
        header[2] == 0x44 && header[3] == 0x46;
}

/// <summary>
/// Valida barcodes decodificando as imagens geradas com ZXing.
/// </summary>
public class BarcodeValidatorService
{
    private readonly ILogger<BarcodeValidatorService> _logger;

    public BarcodeValidatorService(ILogger<BarcodeValidatorService> logger)
    {
        _logger = logger;
    }

    public bool ValidateBarcode(byte[] imageBytes, string expectedValue)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap == null) return false;

            // Converter para RGB luminance para ZXing
            var skiaPixels = bitmap.Pixels;
            var luminances = new byte[bitmap.Width * bitmap.Height];
            for (int i = 0; i < skiaPixels.Length; i++)
            {
                var pixel = skiaPixels[i];
                luminances[i] = (byte)((pixel.Red * 0.299 + pixel.Green * 0.587 + pixel.Blue * 0.114));
            }

            var luminanceSource = new RGBLuminanceSource(luminances, bitmap.Width, bitmap.Height);
            var binaryBitmap = new BinaryBitmap(new HybridBinarizer(luminanceSource));

            var reader = new MultiFormatReader();
            var hints = new Dictionary<DecodeHintType, object>
            {
                [DecodeHintType.TRY_HARDER] = true
            };

            var result = reader.decode(binaryBitmap, hints);
            if (result == null)
            {
                _logger.LogWarning("Barcode não pôde ser decodificado.");
                return false;
            }

            var matches = result.Text.Equals(expectedValue, StringComparison.OrdinalIgnoreCase);
            if (!matches)
                _logger.LogWarning("Barcode decodificado ({Decoded}) ≠ esperado ({Expected}).", result.Text, expectedValue);
            else
                _logger.LogDebug("Barcode validado: {Value}", result.Text);

            return matches;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao validar barcode.");
            return false;
        }
    }
}
