namespace Sl4n;

public sealed class MaskingConfig
{
    /// <summary>
    /// Enable the built-in default rules (email, password, token, credit card, ssn, phone).
    /// Custom <see cref="Rules"/> are always appended on top, whether or not defaults are on.
    /// </summary>
    public bool EnableDefaultRules { get; set; } = true;

    /// <summary>
    /// Declarative custom rules (field-name pattern + strategy), appended to the defaults.
    /// Bindable from configuration. A rule whose <see cref="MaskingRuleConfig.Strategy"/> is
    /// <see cref="MaskingStrategy.Custom"/> cannot carry a function from config and is treated as
    /// <see cref="MaskingStrategy.FullMask"/> (fail-safe over-mask). For a function-based mask,
    /// construct a <see cref="MaskingRule"/> directly.
    /// </summary>
    public List<MaskingRuleConfig> Rules { get; set; } = new();

    /// <summary>
    /// Milliseconds a single custom-rule regex match may run before it is aborted (ReDoS guard).
    /// Default 100. A non-positive value disables the timeout. Built-in <c>[GeneratedRegex]</c>
    /// default rules are linear and not subject to this.
    /// </summary>
    public int RegexTimeoutMs { get; set; } = 100;

    /// <summary>
    /// Invoked when masking a field throws (a custom-mask function error or a regex timeout).
    /// The field is redacted fail-secure to <c>[REDACTED]</c> and logging continues — masking never
    /// throws. Not bindable from configuration; set it on the <see cref="Action{T}"/> config path.
    /// </summary>
    public Action<Exception, string>? OnMaskingError { get; set; }
}
