namespace Sl4n;

/// <summary>
/// A declarative custom masking rule, bindable from configuration.
/// </summary>
public sealed class MaskingRuleConfig
{
    /// <summary>
    /// Regex matched (case-insensitively) against the field <b>name</b>. Anchor it for exact
    /// matches, e.g. <c>^(cvv|cvc|securityCode)$</c>, or use <see cref="MaskKeys.Pattern"/>.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Strategy applied to matched string values. <see cref="MaskingStrategy.Custom"/> is not
    /// expressible from configuration and is treated as <see cref="MaskingStrategy.FullMask"/>.
    /// </summary>
    public MaskingStrategy Strategy { get; set; } = MaskingStrategy.FullMask;
}
