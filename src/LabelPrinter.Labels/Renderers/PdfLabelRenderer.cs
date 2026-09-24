using LabelPrinter.Core.Exceptions;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;
using ZXing.SkiaSharp.Rendering;
using ZXingBarcodeFormat = ZXing.BarcodeFormat;

namespace LabelPrinter.Labels.Renderers;

/// <summary>
/// Renderiza etiquetas como PDF usando QuestPDF.
/// Gera PDFs com dimensões físicas exatas (100×150mm ou 50×30mm).
/// Suporta: texto com acentos, código de barras, QR Code.
/// Nunca usa tamanho A4 — usa o tamanho físico real da etiqueta.
/// </summary>
public class PdfLabelRenderer : ILabelRenderer, IPdfGeneratorService
{
    private readonly ILogger<PdfLabelRenderer> _logger;

    public string RendererName => "PdfLabelRenderer";

    static PdfLabelRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfLabelRenderer(ILogger<PdfLabelRenderer> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> RenderAsync(
        LabelDocument document,
        ILabelTemplate template,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => GeneratePdfBytes(document, template), cancellationToken);
    }

    public async Task<string> GeneratePdfAsync(
        LabelDocument document,
        ILabelTemplate template,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[PdfRenderer] Gerando PDF: {File} ({Width}x{Height}mm)",
            outputPath, template.WidthMm, template.HeightMm);

        var pdfBytes = await RenderAsync(document, template, cancellationToken);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(outputPath, pdfBytes, cancellationToken);

        _logger.LogInformation(
            "[PdfRenderer] PDF gerado: {File} ({Size} bytes)", outputPath, pdfBytes.Length);

        return outputPath;
    }

    private byte[] GeneratePdfBytes(LabelDocument document, ILabelTemplate template)
    {
        try
        {
            float widthPt = (float)(template.WidthMm * 72.0 / 25.4);
            float heightPt = (float)(template.HeightMm * 72.0 / 25.4);
            float marginPt = (float)(template.MarginMm * 72.0 / 25.4);

            var document_pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(widthPt, heightPt, Unit.Point);
                    page.Margin(marginPt, Unit.Point);
                    page.DefaultTextStyle(x => x
                        .FontFamily("Arial")
                        .FontSize(7));

                    page.Content().Column(col =>
                    {
                        col.Spacing(2);

                        // Marketplace / Transportadora
                        if (!string.IsNullOrWhiteSpace(document.Carrier) ||
                            document.Marketplace != Core.Enums.Marketplace.Unknown)
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text(document.Marketplace != Core.Enums.Marketplace.Unknown
                                    ? document.Marketplace.ToString().ToUpperInvariant()
                                    : "")
                                    .Bold().FontSize(8);

                                if (!string.IsNullOrWhiteSpace(document.Carrier))
                                    row.RelativeItem().AlignRight().Text(document.Carrier).FontSize(7);
                            });
                            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        }

                        // Barcode
                        var barcodeValue = document.BarcodeValue ?? document.TrackingNumber;
                        if (!string.IsNullOrWhiteSpace(barcodeValue))
                        {
                            var barcodeImage = GenerateBarcodeImage(barcodeValue!, template);
                            if (barcodeImage != null)
                            {
                                col.Item().AlignCenter().Image(barcodeImage).FitWidth();
                                col.Item().AlignCenter().Text(barcodeValue).FontSize(6);
                            }
                            else
                            {
                                col.Item().AlignCenter().Text(barcodeValue).FontSize(7).Bold();
                            }
                        }

                        // QR Code
                        if (!string.IsNullOrWhiteSpace(document.QrCodeValue))
                        {
                            var qrImage = GenerateQrCodeImage(document.QrCodeValue!, template);
                            if (qrImage != null)
                            {
                                col.Item().Row(row =>
                                {
                                    row.AutoItem().Width(template.WidthMm > 60 ? 40 : 25, Unit.Point)
                                        .Image(qrImage);
                                    row.RelativeItem();
                                });
                            }
                        }

                        // Destinatário
                        col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        col.Item().Text("DESTINATÁRIO").FontSize(6).Bold();

                        if (!string.IsNullOrWhiteSpace(document.RecipientName))
                            col.Item().Text(document.RecipientName).FontSize(8).Bold();

                        if (!string.IsNullOrWhiteSpace(document.AddressLine1))
                        {
                            var addressParts = new List<string> { document.AddressLine1 };
                            if (!string.IsNullOrWhiteSpace(document.AddressLine2))
                                addressParts.Add(document.AddressLine2);
                            if (!string.IsNullOrWhiteSpace(document.AddressComplement))
                                addressParts.Add(document.AddressComplement);
                            col.Item().Text(string.Join(", ", addressParts)).FontSize(7);
                        }

                        if (!string.IsNullOrWhiteSpace(document.Neighborhood))
                            col.Item().Text(document.Neighborhood).FontSize(7);

                        var locationParts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(document.City))
                            locationParts.Add(document.City);
                        if (!string.IsNullOrWhiteSpace(document.State))
                            locationParts.Add(document.State);
                        if (locationParts.Count > 0)
                            col.Item().Text(string.Join(" - ", locationParts)).FontSize(7);

                        if (!string.IsNullOrWhiteSpace(document.PostalCode))
                            col.Item().Text($"CEP: {document.PostalCode}").FontSize(7).Bold();

                        // Remetente
                        if (!string.IsNullOrWhiteSpace(document.SenderName))
                        {
                            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                            col.Item().Text("REMETENTE").FontSize(6).Bold();
                            col.Item().Text(document.SenderName).FontSize(6);
                        }

                        // Pedido + Volume
                        col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        col.Item().Row(row =>
                        {
                            if (!string.IsNullOrWhiteSpace(document.OrderReference))
                                row.RelativeItem()
                                    .Text($"Pedido: {document.OrderReference}")
                                    .FontSize(6);
                            if (document.PackageCount > 1)
                                row.AutoItem()
                                    .Text($"Vol: {document.PackageNumber}/{document.PackageCount}")
                                    .FontSize(6).Bold();
                        });
                    });
                });
            });

            return document_pdf.GeneratePdf();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PdfRenderer] Erro ao gerar PDF.");
            throw new PdfGenerationException($"Erro ao gerar PDF: {ex.Message}", ex);
        }
    }

    private byte[]? GenerateBarcodeImage(string value, ILabelTemplate template)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            int width = (int)(template.WidthMm * 203.0 / 25.4 * 0.85);
            int height = Math.Max((int)(template.HeightMm * 0.18 * 203.0 / 25.4), 60);

            var writer = new BarcodeWriterPixelData
            {
                Format = ZXingBarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = 5,
                    PureBarcode = false
                }
            };

            var pixelData = writer.Write(value);
            return ConvertPixelDataToPng(pixelData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao gerar barcode para: {Value}", value);
            return null;
        }
    }

    private byte[]? GenerateQrCodeImage(string value, ILabelTemplate template)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            int sizePx = Math.Max((int)(Math.Min(template.WidthMm, template.HeightMm) * 0.3 * 203.0 / 25.4), 80);

            var writer = new BarcodeWriterPixelData
            {
                Format = ZXingBarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = sizePx,
                    Height = sizePx,
                    Margin = 4,
                    ErrorCorrection = ErrorCorrectionLevel.M
                }
            };

            var pixelData = writer.Write(value);
            return ConvertPixelDataToPng(pixelData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao gerar QR Code para: {Value}", value);
            return null;
        }
    }

    private static byte[]? ConvertPixelDataToPng(PixelData pixelData)
    {
        try
        {
            using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            unsafe
            {
                fixed (byte* ptr = pixelData.Pixels)
                {
                    bitmap.SetPixels((nint)ptr);
                }
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
