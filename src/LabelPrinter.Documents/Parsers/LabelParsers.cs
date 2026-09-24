using System.Text.RegularExpressions;
using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using LabelPrinter.Core.Models;
using Microsoft.Extensions.Logging;

namespace LabelPrinter.Documents.Parsers;

/// <summary>
/// Parser genérico de arquivos TXT/CSV.
/// Detecta campos de etiqueta por palavras-chave e padrões comuns.
/// Serve como fallback quando parsers específicos não conseguem interpretar.
/// </summary>
public class TxtLabelParser : ILabelParser
{
    private readonly ILogger<TxtLabelParser> _logger;
    private readonly IEncodingDetector _encodingDetector;
    private readonly IMarketplaceDetector _marketplaceDetector;

    public string ParserName => "TxtLabelParser";

    // Padrões de campos conhecidos (key: value ou key=value)
    private static readonly Dictionary<string, string[]> FieldPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TrackingNumber"]     = ["tracking", "rastreio", "rastreamento", "codigo_rastreamento", "track_number", "codigo de rastreio"],
        ["OrderReference"]     = ["pedido", "order", "order_id", "numero_pedido", "order_number", "numpedido"],
        ["RecipientName"]      = ["destinatario", "recipient", "nome", "name", "cliente", "customer"],
        ["AddressLine1"]       = ["endereco", "address", "logradouro", "rua", "av.", "avenida"],
        ["AddressLine2"]       = ["numero", "number", "num", "n\\."],
        ["AddressComplement"]  = ["complemento", "complement", "apto", "apartamento"],
        ["Neighborhood"]       = ["bairro", "neighborhood", "district"],
        ["City"]               = ["cidade", "city", "municipio"],
        ["State"]              = ["estado", "state", "uf"],
        ["PostalCode"]         = ["cep", "postal", "zip", "zipcode", "codigo_postal"],
        ["BarcodeValue"]       = ["codigo_barras", "barcode", "cod_barras", "ean", "gs1"],
        ["QrCodeValue"]        = ["qrcode", "qr_code", "qr", "qr code"],
        ["SenderName"]         = ["remetente", "sender", "loja", "vendedor"],
        ["Carrier"]            = ["transportadora", "carrier", "logistica", "frete"],
        ["WeightGrams"]        = ["peso", "weight", "kg", "gramas"]
    };

    // Regex para detectar CEP brasileiro
    private static readonly Regex CepRegex = new(@"\b\d{5}-?\d{3}\b", RegexOptions.Compiled);

    // Regex para detectar rastreamento (ex: BR123456789BR, AA123456789AA)
    private static readonly Regex TrackingRegex = new(
        @"\b[A-Z]{2}\d{9}[A-Z]{2}\b|\b\d{20,26}\b", RegexOptions.Compiled);

    // Regex para barcode numérico longo
    private static readonly Regex BarcodeRegex = new(@"\b\d{8,25}\b", RegexOptions.Compiled);

    public TxtLabelParser(
        ILogger<TxtLabelParser> logger,
        IEncodingDetector encodingDetector,
        IMarketplaceDetector marketplaceDetector)
    {
        _logger = logger;
        _encodingDetector = encodingDetector;
        _marketplaceDetector = marketplaceDetector;
    }

    public bool CanParse(string filePath, string? content = null)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".txt" or ".csv";
    }

    public async Task<LabelDocument?> ParseAsync(
        string filePath,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{Parser}] Iniciando parsing: {File}", ParserName, filePath);

        var detectedEncoding = encoding ?? _encodingDetector.DetectEncoding(filePath);
        _logger.LogInformation("[{Parser}] Encoding: {Encoding}", ParserName, detectedEncoding.WebName);

        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath, detectedEncoding, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Parser}] Erro ao ler arquivo: {File}", ParserName, filePath);
            return null;
        }

        var document = new LabelDocument
        {
            SourceFileName = Path.GetFileName(filePath),
            DetectedEncoding = detectedEncoding.WebName,
            RawContent = content.Length > 10_000 ? content[..10_000] : content,
            Marketplace = _marketplaceDetector.DetectMarketplace(Path.GetFileName(filePath), content)
        };

        // Detectar campos pelos padrões
        ParseFields(content, document);

        if (content.Contains("^XA", StringComparison.OrdinalIgnoreCase) && 
            content.Contains("^XZ", StringComparison.OrdinalIgnoreCase))
        {
            document.IsRawZpl = true;
            _logger.LogInformation("[{Parser}] ZPL bruto detectado. PDF não será gerado.", ParserName);
        }

        // Tentar detectar marketplace-específico com parser dedicado
        _logger.LogInformation(
            "[{Parser}] Parsing concluído. Marketplace: {Marketplace}, Tracking: {Tracking}, Destinatário: {Name}",
            ParserName,
            document.Marketplace,
            document.TrackingNumber ?? "(não detectado)",
            document.RecipientName ?? "(não detectado)");

        return document;
    }

    private void ParseFields(string content, LabelDocument document)
    {
        var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Tentar parsing como key: value ou key=value
            TryParseKeyValue(trimmed, document);
        }

        // Detecção por regex no conteúdo completo
        if (string.IsNullOrEmpty(document.TrackingNumber))
        {
            var trackMatch = TrackingRegex.Match(content);
            if (trackMatch.Success)
                document.TrackingNumber = trackMatch.Value;
        }

        if (string.IsNullOrEmpty(document.PostalCode))
        {
            var cepMatch = CepRegex.Match(content);
            if (cepMatch.Success)
                document.PostalCode = cepMatch.Value;
        }

        // Detectar tamanho de etiqueta baseado em campos
        document.LabelSize = DetermineLabelSize(document);
    }

    private static void TryParseKeyValue(string line, LabelDocument document)
    {
        // Suporta: "key: value", "key=value", "key | value", "key;value"
        string? key = null, value = null;

        foreach (var separator in new[] { ':', '=', '|', ';', '\t' })
        {
            var idx = line.IndexOf(separator);
            if (idx > 0 && idx < line.Length - 1)
            {
                key = line[..idx].Trim();
                value = line[(idx + 1)..].Trim();
                break;
            }
        }

        if (key == null || value == null || string.IsNullOrWhiteSpace(value))
            return;

        // Mapear para campo do documento
        if (MatchesField(key, "TrackingNumber") && string.IsNullOrEmpty(document.TrackingNumber))
            document.TrackingNumber = value;
        else if (MatchesField(key, "OrderReference") && string.IsNullOrEmpty(document.OrderReference))
            document.OrderReference = value;
        else if (MatchesField(key, "RecipientName") && string.IsNullOrEmpty(document.RecipientName))
            document.RecipientName = value;
        else if (MatchesField(key, "AddressLine1") && string.IsNullOrEmpty(document.AddressLine1))
            document.AddressLine1 = value;
        else if (MatchesField(key, "Neighborhood") && string.IsNullOrEmpty(document.Neighborhood))
            document.Neighborhood = value;
        else if (MatchesField(key, "City") && string.IsNullOrEmpty(document.City))
            document.City = value;
        else if (MatchesField(key, "State") && string.IsNullOrEmpty(document.State))
            document.State = value.Length > 2 ? value : value.ToUpperInvariant();
        else if (MatchesField(key, "PostalCode") && string.IsNullOrEmpty(document.PostalCode))
            document.PostalCode = value;
        else if (MatchesField(key, "BarcodeValue") && string.IsNullOrEmpty(document.BarcodeValue))
            document.BarcodeValue = value;
        else if (MatchesField(key, "SenderName") && string.IsNullOrEmpty(document.SenderName))
            document.SenderName = value;
        else if (MatchesField(key, "Carrier") && string.IsNullOrEmpty(document.Carrier))
            document.Carrier = value;
        else
            document.AdditionalFields[key] = value; // Preservar campos não mapeados
    }

    private static bool MatchesField(string key, string fieldName) =>
        FieldPatterns.TryGetValue(fieldName, out var patterns) &&
        patterns.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static LabelSize DetermineLabelSize(LabelDocument document)
    {
        // Inferir tamanho pelo marketplace e campos disponíveis
        if (!string.IsNullOrEmpty(document.TrackingNumber) ||
            !string.IsNullOrEmpty(document.RecipientName))
        {
            return LabelSize.Standard100x150;
        }

        return LabelSize.Standard100x150; // Default
    }
}

/// <summary>
/// Parser específico para etiquetas da Shopee.
/// Shopee pode enviar ZIPs com PDFs já formatados ou TXTs com dados estruturados.
/// </summary>
public class ShopeeLabelParser : ILabelParser
{
    private readonly ILogger<ShopeeLabelParser> _logger;
    private readonly TxtLabelParser _baseParser;

    public string ParserName => "ShopeeLabelParser";

    // Campos específicos da Shopee
    private static readonly Dictionary<string, string> ShopeeFieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["order_sn"] = "OrderReference",
        ["tracking_no"] = "TrackingNumber",
        ["recipient_name"] = "RecipientName",
        ["full_address"] = "AddressLine1",
        ["city"] = "City",
        ["state"] = "State",
        ["zipcode"] = "PostalCode",
        ["shop_name"] = "SenderName",
        ["logistics_channel_name"] = "Carrier"
    };

    public ShopeeLabelParser(
        ILogger<ShopeeLabelParser> logger,
        TxtLabelParser baseParser)
    {
        _logger = logger;
        _baseParser = baseParser;
    }

    public bool CanParse(string filePath, string? content = null)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        if (fileName.Contains("shopee") || fileName.Contains("spx"))
            return true;

        if (content != null &&
            (content.Contains("shopee", StringComparison.OrdinalIgnoreCase) ||
             content.Contains("order_sn", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public async Task<LabelDocument?> ParseAsync(
        string filePath,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{Parser}] Parsing etiqueta Shopee: {File}", ParserName, filePath);

        // Usar parser base e depois aplicar lógica específica da Shopee
        var document = await _baseParser.ParseAsync(filePath, encoding, cancellationToken);
        if (document == null) return null;

        document.Marketplace = Marketplace.Shopee;
        document.LabelSize = LabelSize.Standard100x150;

        // Processar campos específicos da Shopee que podem estar nos AdditionalFields
        foreach (var (shopeeKey, fieldName) in ShopeeFieldMap)
        {
            if (document.AdditionalFields.TryGetValue(shopeeKey, out var shopeeValue))
            {
                ApplyShopeeField(document, fieldName, shopeeValue);
                document.AdditionalFields.Remove(shopeeKey);
            }
        }

        _logger.LogInformation(
            "[{Parser}] Shopee parsing concluído. Pedido: {Order}, Tracking: {Tracking}",
            ParserName, document.OrderReference, document.TrackingNumber);

        return document;
    }

    private static void ApplyShopeeField(LabelDocument doc, string fieldName, string value)
    {
        switch (fieldName)
        {
            case "OrderReference": doc.OrderReference = value; break;
            case "TrackingNumber": doc.TrackingNumber = value; break;
            case "RecipientName": doc.RecipientName = value; break;
            case "AddressLine1": doc.AddressLine1 = value; break;
            case "City": doc.City = value; break;
            case "State": doc.State = value; break;
            case "PostalCode": doc.PostalCode = value; break;
            case "SenderName": doc.SenderName = value; break;
            case "Carrier": doc.Carrier = value; break;
        }
    }
}

/// <summary>
/// Parser para etiquetas do Mercado Livre.
/// </summary>
public class MercadoLivreLabelParser : ILabelParser
{
    private readonly ILogger<MercadoLivreLabelParser> _logger;
    private readonly TxtLabelParser _baseParser;

    public string ParserName => "MercadoLivreLabelParser";

    public MercadoLivreLabelParser(
        ILogger<MercadoLivreLabelParser> logger,
        TxtLabelParser baseParser)
    {
        _logger = logger;
        _baseParser = baseParser;
    }

    public bool CanParse(string filePath, string? content = null)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        if (fileName.Contains("mercado") || fileName.Contains("meli") || fileName.Contains("ml-"))
            return true;

        if (content != null &&
            (content.Contains("mercadolivre", StringComparison.OrdinalIgnoreCase) ||
             content.Contains("MELI", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public async Task<LabelDocument?> ParseAsync(
        string filePath,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{Parser}] Parsing etiqueta Mercado Livre: {File}", ParserName, filePath);

        var document = await _baseParser.ParseAsync(filePath, encoding, cancellationToken);
        if (document == null) return null;

        document.Marketplace = Marketplace.MercadoLivre;
        document.LabelSize = LabelSize.Standard100x150;

        return document;
    }
}

/// <summary>
/// Parser para etiquetas da Amazon.
/// </summary>
public class AmazonLabelParser : ILabelParser
{
    private readonly ILogger<AmazonLabelParser> _logger;
    private readonly TxtLabelParser _baseParser;

    public string ParserName => "AmazonLabelParser";

    public AmazonLabelParser(
        ILogger<AmazonLabelParser> logger,
        TxtLabelParser baseParser)
    {
        _logger = logger;
        _baseParser = baseParser;
    }

    public bool CanParse(string filePath, string? content = null)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        if (fileName.Contains("amazon") || fileName.Contains("amz") ||
            fileName.Contains("fba") || fileName.Contains("mfn"))
            return true;

        if (content != null &&
            (content.Contains("amazon.com", StringComparison.OrdinalIgnoreCase) ||
             content.Contains("ASIN", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public async Task<LabelDocument?> ParseAsync(
        string filePath,
        System.Text.Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{Parser}] Parsing etiqueta Amazon: {File}", ParserName, filePath);

        var document = await _baseParser.ParseAsync(filePath, encoding, cancellationToken);
        if (document == null) return null;

        document.Marketplace = Marketplace.Amazon;
        document.LabelSize = LabelSize.Standard100x150;

        return document;
    }
}
