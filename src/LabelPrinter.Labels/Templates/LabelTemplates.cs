using LabelPrinter.Core.Configuration;
using LabelPrinter.Core.Interfaces;

namespace LabelPrinter.Labels.Templates;

/// <summary>
/// Template de etiqueta 100 × 150 mm — padrão logístico.
/// </summary>
public class Standard100x150Template : ILabelTemplate
{
    public string TemplateName => "Standard100x150";
    public double WidthMm => 100.0;
    public double HeightMm => 150.0;
    public double MarginMm => 3.0;
}

/// <summary>
/// Template de etiqueta 50 × 30 mm — produto pequeno.
/// </summary>
public class Small50x30Template : ILabelTemplate
{
    public string TemplateName => "Small50x30";
    public double WidthMm => 50.0;
    public double HeightMm => 30.0;
    public double MarginMm => 1.5;
}

/// <summary>
/// Template customizado configurado via appsettings.json.
/// </summary>
public class CustomLabelTemplate : ILabelTemplate
{
    public string TemplateName => "Custom";
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public double MarginMm { get; init; } = 2.0;

    public static CustomLabelTemplate FromConfig(LabelTemplateConfig config) => new()
    {
        WidthMm = config.WidthMm,
        HeightMm = config.HeightMm,
        MarginMm = config.MarginMm
    };
}

/// <summary>
/// Resolve o template correto baseado no tamanho da etiqueta.
/// </summary>
public class LabelTemplateResolver
{
    private readonly LabelOptions _options;
    private readonly Standard100x150Template _standard100x150 = new();
    private readonly Small50x30Template _small50x30 = new();

    public LabelTemplateResolver(Microsoft.Extensions.Options.IOptions<LabelOptions> options)
    {
        _options = options.Value;
    }

    public ILabelTemplate Resolve(Core.Enums.LabelSize labelSize) => labelSize switch
    {
        Core.Enums.LabelSize.Standard100x150 => _standard100x150,
        Core.Enums.LabelSize.Small50x30 => _small50x30,
        Core.Enums.LabelSize.Custom => CustomLabelTemplate.FromConfig(_options.Standard100x150),
        _ => _standard100x150
    };
}
