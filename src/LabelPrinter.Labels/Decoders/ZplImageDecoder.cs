using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace LabelPrinter.Labels.Decoders;

public class ZplImageDecoder
{
    private readonly ILogger<ZplImageDecoder> _logger;

    // Procura por ~DGR:NomeDaImagem.GRF,TotalBytes,BytesPorLinha,:Z64:DadosBase64
    // Também detecta CRC se houver
    private static readonly Regex DgrRegex = new Regex(
        @"~DG[A-Z0-9:]+\.GRF,\s*(?<totalBytes>\d+)\s*,\s*(?<bytesPerRow>\d+)\s*,:Z64:(?<b64>[a-zA-Z0-9+/=]+)(?<crc>[a-fA-F0-9]{4})?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ZplImageDecoder(ILogger<ZplImageDecoder> logger)
    {
        _logger = logger;
    }

    public bool IsZplGraphic(string content)
    {
        return DgrRegex.IsMatch(content);
    }

    public async Task<string> ConvertZplToPdfAsync(string zplContent, string outputPdfPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analisando blocos gráficos ZPL (Z64) para conversão em PDF.");

        var matches = DgrRegex.Matches(zplContent);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException("Nenhum bloco gráfico Z64 encontrado no arquivo ZPL.");
        }

        var images = new List<byte[]>();

        foreach (Match match in matches)
        {
            int totalBytes = int.Parse(match.Groups["totalBytes"].Value);
            int bytesPerRow = int.Parse(match.Groups["bytesPerRow"].Value);
            string base64Data = match.Groups["b64"].Value;

            try
            {
                byte[] pngBytes = DecodeZ64ToPng(base64Data, totalBytes, bytesPerRow);
                images.Add(pngBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao decodificar um bloco Z64.");
            }
        }

        if (images.Count == 0)
        {
            throw new InvalidOperationException("Não foi possível decodificar nenhuma imagem do arquivo ZPL.");
        }

        // Usar QuestPDF para colocar todas as imagens decodificadas em páginas
        var document = Document.Create(container =>
        {
            foreach (var imgBytes in images)
            {
                container.Page(page =>
                {
                    page.Margin(0);
                    // O tamanho deve ser exatamente 100x150mm
                    page.Size(100, 150, Unit.Millimetre);
                    page.Content().Image(imgBytes).FitArea();
                });
            }
        });

        // Garantir que a pasta de destino exista
        var dir = Path.GetDirectoryName(outputPdfPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        document.GeneratePdf(outputPdfPath);
        _logger.LogInformation("Gerado PDF com {Pages} página(s) a partir de ZPL Z64: {File}", images.Count, outputPdfPath);

        return await Task.FromResult(outputPdfPath);
    }

    private byte[] DecodeZ64ToPng(string base64Data, int expectedTotalBytes, int bytesPerRow)
    {
        // 1. Decodificar Base64
        byte[] compressedData = Convert.FromBase64String(base64Data);

        // 2. Descomprimir Zlib
        byte[] rawGrfBytes = new byte[expectedTotalBytes];
        
        // Zlib header (2 bytes) tem que ser ignorado pelo DeflateStream
        // Zlib stream starts with 78 9C (or similar). We skip first 2 bytes.
        using (var ms = new MemoryStream(compressedData, 2, compressedData.Length - 2))
        {
            using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int totalRead = 0;
                while (totalRead < expectedTotalBytes)
                {
                    int read = deflate.Read(rawGrfBytes, totalRead, expectedTotalBytes - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
            }
        }

        // 3. Converter GRF 1-bit raster para SKBitmap
        int height = expectedTotalBytes / bytesPerRow;
        int width = bytesPerRow * 8; // Cada byte = 8 pixels

        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        
        unsafe
        {
            uint* pixels = (uint*)bitmap.GetPixels().ToPointer();

            int pixelIndex = 0;
            for (int y = 0; y < height; y++)
            {
                for (int xByte = 0; xByte < bytesPerRow; xByte++)
                {
                    byte b = rawGrfBytes[y * bytesPerRow + xByte];

                    // Processar 8 bits (1 = preto, 0 = branco)
                    for (int bit = 7; bit >= 0; bit--)
                    {
                        bool isBlack = ((b >> bit) & 1) == 1;
                        pixels[pixelIndex++] = isBlack ? 0xFF000000 : 0xFFFFFFFF; // ARGB
                    }
                }
            }
        }

        // 4. Engrossar a imagem (efeito Negrito) e codificar para PNG
        using var finalBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(finalBitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint();
            // Desenha a original
            canvas.DrawBitmap(bitmap, 0, 0, paint);
            // Engrossa 1 pixel para os lados (negrito/darkness++)
            canvas.DrawBitmap(bitmap, 1, 0, paint);
            canvas.DrawBitmap(bitmap, 0, 1, paint);
            canvas.DrawBitmap(bitmap, 1, 1, paint);
        }

        using var image = SKImage.FromBitmap(finalBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
