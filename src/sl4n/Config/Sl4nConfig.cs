namespace Sl4n;

public sealed class Sl4nConfig
{
    public const string SectionName = "sl4n";

    public MaskingConfig Masking { get; set; } = new();
    public ContextConfig Context { get; set; } = new();

    /// <summary>
    /// Declarative per-level whitelist of context (scope) fields. Keys are MEL level names
    /// (<c>Trace</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>, <c>Error</c>, <c>Critical</c>)
    /// plus <c>default</c> (fallback for any level not listed); matched case-insensitively.
    /// A value of <c>["*"]</c> allows every context field. Empty = no filtering (all fields pass).
    /// Per-call structured state is never filtered by the matrix — only context/scope fields are.
    /// </summary>
    public Dictionary<string, string[]> LoggingMatrix { get; set; } = new();
}
