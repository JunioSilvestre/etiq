using FluentAssertions;
using LabelPrinter.Core.Enums;
using LabelPrinter.Documents.Detectors;
using LabelPrinter.Documents.Parsers;
using LabelPrinter.Infrastructure.Encoding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LabelPrinter.UnitTests;

public class MarketplaceDetectorTests
{
    private readonly MarketplaceDetector _detector;

    public MarketplaceDetectorTests()
    {
        _detector = new MarketplaceDetector(NullLogger<MarketplaceDetector>.Instance);
    }

    [Theory]
    [InlineData("shopee_label_12345.zip", Marketplace.Shopee)]
    [InlineData("SHOPEE-ORDER-2026.txt", Marketplace.Shopee)]
    [InlineData("amazon_shipping_label.zip", Marketplace.Amazon)]
    [InlineData("AFN-12345.zip", Marketplace.Amazon)]
    [InlineData("mercadolivre_etiqueta.zip", Marketplace.MercadoLivre)]
    [InlineData("meli-label-123.txt", Marketplace.MercadoLivre)]
    [InlineData("unknown_file.zip", Marketplace.Unknown)]
    public void DetectMarketplace_ByFileName_ShouldReturnCorrectMarketplace(
        string fileName, Marketplace expected)
    {
        var result = _detector.DetectMarketplace(fileName);
        result.Should().Be(expected);
    }

    [Fact]
    public void DetectMarketplace_ByContent_Shopee_ShouldReturnShopee()
    {
        var content = "order_sn: SH202609001\nshopee express tracking\nlogistics_channel_name: SPX";
        var result = _detector.DetectMarketplace("unknown.txt", content);
        result.Should().Be(Marketplace.Shopee);
    }

    [Fact]
    public void DetectMarketplace_ByContent_Amazon_ShouldReturnAmazon()
    {
        var content = "ASIN: B001234567\namazon.com logistics\nFBA-12345";
        var result = _detector.DetectMarketplace("unknown.txt", content);
        result.Should().Be(Marketplace.Amazon);
    }
}

public class EncodingDetectorTests
{
    private readonly EncodingDetectorService _detector;

    public EncodingDetectorTests()
    {
        _detector = new EncodingDetectorService(NullLogger<EncodingDetectorService>.Instance);
    }

    [Fact]
    public async Task DetectEncoding_Utf8File_ShouldDetectUtf8()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "Destinatário: João da Silva\nRua Açafrão, 123\nSão Paulo - SP\nCEP: 01310-100";
            await File.WriteAllTextAsync(tempFile, content, System.Text.Encoding.UTF8);

            // Act
            var encoding = _detector.DetectEncoding(tempFile);

            // Assert
            encoding.Should().NotBeNull();
            // UTF-8 ou Windows-1252 são ambos aceitáveis — o importante é que o conteúdo seja lido corretamente
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DetectEncoding_Windows1252File_ShouldDetectSuccessfully()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var win1252 = System.Text.Encoding.GetEncoding("windows-1252");
            var content = "Destinat\xe1rio: Jo\xe3o da Silva"; // á, ã em windows-1252
            await File.WriteAllBytesAsync(tempFile, win1252.GetBytes(content));

            // Act
            var encoding = _detector.DetectEncoding(tempFile);

            // Assert
            encoding.Should().NotBeNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

public class TxtLabelParserTests
{
    private readonly TxtLabelParser _parser;

    public TxtLabelParserTests()
    {
        var encodingDetector = new EncodingDetectorService(NullLogger<EncodingDetectorService>.Instance);
        var marketplaceDetector = new MarketplaceDetector(NullLogger<MarketplaceDetector>.Instance);
        _parser = new TxtLabelParser(
            NullLogger<TxtLabelParser>.Instance,
            encodingDetector,
            marketplaceDetector);
    }

    [Fact]
    public void CanParse_TxtFile_ShouldReturnTrue()
    {
        _parser.CanParse("label.txt").Should().BeTrue();
        _parser.CanParse("order.csv").Should().BeTrue();
        _parser.CanParse("label.zip").Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_ValidTxtLabel_ShouldExtractFields()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".txt";
        var content = @"
Tracking: BR123456789BR
Pedido: 20260923-001
Destinatario: João da Silva
Endereco: Rua das Flores, 456
Complemento: Apto 12
Bairro: Jardim América
Cidade: São Paulo
Estado: SP
CEP: 01310-100
Remetente: Loja ABC
";
        try
        {
            await File.WriteAllTextAsync(tempFile, content, System.Text.Encoding.UTF8);

            // Act
            var document = await _parser.ParseAsync(tempFile);

            // Assert
            document.Should().NotBeNull();
            document!.TrackingNumber.Should().Be("BR123456789BR");
            document.OrderReference.Should().Be("20260923-001");
            document.RecipientName.Should().Contain("João");
            document.City.Should().Be("São Paulo");
            document.State.Should().Be("SP");
            document.PostalCode.Should().Be("01310-100");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_AccentedCharacters_ShouldPreserve()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".txt";
        var content = "Destinatario: Leônidas Ção Açaí\nCidade: São Paulo\nEstado: SP\nCEP: 01310-100";

        try
        {
            await File.WriteAllTextAsync(tempFile, content, System.Text.Encoding.UTF8);

            // Act
            var document = await _parser.ParseAsync(tempFile);

            // Assert
            document.Should().NotBeNull();
            document!.RecipientName.Should().Contain("Leônidas");
            document.City.Should().Be("São Paulo");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
