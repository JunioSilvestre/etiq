using LabelPrinter.Core.Enums;
using LabelPrinter.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LabelPrinter.Documents.Detectors;

/// <summary>
/// Detecta o marketplace de origem da etiqueta.
/// Usa nome do arquivo e conteúdo como sinais — não depende exclusivamente do nome.
/// Extensível: cada marketplace tem seu conjunto de padrões.
/// </summary>
public class MarketplaceDetector : IMarketplaceDetector
{
    private readonly ILogger<MarketplaceDetector> _logger;

    // Padrões de nome de arquivo por marketplace
    private static readonly Dictionary<Marketplace, string[]> FileNamePatterns = new()
    {
        [Marketplace.Amazon] = ["amazon", "amz", "afn-", "fba-", "mfn-"],
        [Marketplace.Shopee] = ["shopee", "spx", "shopee_", "shopee-"],
        [Marketplace.MercadoLivre] = ["mercadolivre", "mercado_livre", "meli", "ml-", "nf-e", "mercadopago"],
        [Marketplace.Correios] = ["correios", "sigep", "pac-", "sedex-", "carta-"],
        [Marketplace.Jadlog] = ["jadlog", "jad-"],
        [Marketplace.Loggi] = ["loggi"],
        [Marketplace.MelhorEnvio] = ["melhorenvio", "melhor_envio", "me-label"]
    };

    // Padrões de conteúdo por marketplace
    private static readonly Dictionary<Marketplace, string[]> ContentPatterns = new()
    {
        [Marketplace.Amazon] = ["amazon.com", "fulfillment by amazon", "amazon logistics", "ASIN", "amz-fulfillment"],
        [Marketplace.Shopee] = ["shopee", "spx", "shopee express", "shopee brasil"],
        [Marketplace.MercadoLivre] = ["mercadolivre", "mercado livre", "mercadopago", "MELI"],
        [Marketplace.Correios] = ["correios", "sigep", "SEDEX", "PAC", "ECT"],
        [Marketplace.Jadlog] = ["jadlog"],
        [Marketplace.Loggi] = ["loggi"],
        [Marketplace.MelhorEnvio] = ["melhorenvio", "melhor envio"]
    };

    public MarketplaceDetector(ILogger<MarketplaceDetector> logger)
    {
        _logger = logger;
    }

    public Marketplace DetectMarketplace(string fileName, string? content = null)
    {
        var fileNameLower = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        // 1. Tentar pelo nome do arquivo
        foreach (var (marketplace, patterns) in FileNamePatterns)
        {
            if (patterns.Any(p => fileNameLower.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("Marketplace detectado pelo nome do arquivo: {Marketplace} (arquivo: {File})",
                    marketplace, fileName);
                return marketplace;
            }
        }

        // 2. Tentar pelo conteúdo se disponível
        if (!string.IsNullOrWhiteSpace(content))
        {
            foreach (var (marketplace, patterns) in ContentPatterns)
            {
                if (patterns.Any(p => content.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug("Marketplace detectado pelo conteúdo: {Marketplace} (arquivo: {File})",
                        marketplace, fileName);
                    return marketplace;
                }
            }
        }

        _logger.LogWarning("Marketplace não identificado para arquivo: {File}. Usando Unknown.", fileName);
        return Marketplace.Unknown;
    }
}
