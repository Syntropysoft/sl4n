namespace Sl4n;

/// <summary>
/// Everything sl4n is told at startup. Bind it from configuration with
/// <c>AddSl4n(configuration)</c>, or build it in code with <c>AddSl4n(cfg =&gt; …)</c> — the code
/// path is the AOT-safe one, since binding uses reflection.
/// </summary>
public sealed class Sl4nConfig
{
    /// <summary>Conventional configuration section name: <c>"sl4n"</c>.</summary>
    public const string SectionName = "sl4n";

    /// <summary>PII masking: default rules, custom rules, the ReDoS timeout and the failure hook.</summary>
    public MaskingConfig Masking { get; set; } = new();

    /// <summary>Context propagation: inbound/outbound header maps and auto-generated fields.</summary>
    public ContextConfig Context { get; set; } = new();

    /// <summary>
    /// Declarative per-level whitelist of context (scope) fields. Keys are MEL level names
    /// (<c>Trace</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>, <c>Error</c>, <c>Critical</c>)
    /// plus <c>default</c> (fallback for any level not listed); matched case-insensitively.
    /// A value of <c>["*"]</c> allows every context field. Empty = no filtering (all fields pass).
    /// Per-call structured state is never filtered by the matrix — only context/scope fields are.
    /// </summary>
    public Dictionary<string, string[]> LoggingMatrix { get; set; } = new();

    /// <summary>
    /// Invoked when a transport's <c>Log</c> throws. The failure is isolated (other transports still
    /// receive the entry, the worker keeps running) and counted in <see cref="Sl4nStats"/>; the
    /// second argument is the transport's type name. Not bindable from configuration.
    /// </summary>
    public Action<Exception, string>? OnLogFailure { get; set; }

    /// <summary>
    /// Named retention policies (compliance metadata). Tag a log with one via
    /// <see cref="Sl4nRetention.BeginRetentionScope"/>; the emitted entry then carries
    /// <c>retention</c> / <c>retentionClass</c> / <c>retentionDays</c>. Bindable from configuration.
    /// </summary>
    public Dictionary<string, RetentionPolicy> RetentionPolicies { get; set; } = new();
}
