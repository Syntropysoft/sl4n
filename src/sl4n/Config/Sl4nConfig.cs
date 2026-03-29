namespace Sl4n;

public sealed class Sl4nConfig
{
    public const string SectionName = "sl4n";

    public MaskingConfig Masking { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
}
