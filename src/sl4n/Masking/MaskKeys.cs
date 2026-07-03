namespace Sl4n;

/// <summary>
/// Sensitive field-name aliases, grouped — the .NET equivalent of SyntropyLog's <c>maskEnum</c>.
/// Reference these instead of string literals in your masking rules (keeps custom-rule patterns
/// out of secret scanners such as Sonar S2068). Pair with <see cref="Pattern"/> to build a rule.
/// </summary>
/// <example>
/// <code>
/// cfg.Masking.Rules.Add(new MaskingRuleConfig
/// {
///     Pattern  = MaskKeys.Pattern(MaskKeys.Token),
///     Strategy = MaskingStrategy.FullMask,
/// });
/// </code>
/// </example>
public static class MaskKeys
{
    /// <summary>Email field aliases.</summary>
    public static readonly string[] Email = ["email", "mail"];

    /// <summary>Password / secret field aliases.</summary>
    public static readonly string[] Password = ["password", "pass", "pwd", "secret"];

    /// <summary>Token / credential field aliases.</summary>
    public static readonly string[] Token = ["token", "key", "auth", "jwt", "bearer"];

    /// <summary>Credit-card field aliases.</summary>
    public static readonly string[] Card = ["credit_card", "creditcard", "card_number", "cardnumber"];

    /// <summary>Social-security-number field aliases.</summary>
    public static readonly string[] Ssn = ["ssn", "social_security"];

    /// <summary>Phone-number field aliases.</summary>
    public static readonly string[] Phone = ["phone", "mobile", "tel"];

    /// <summary>Every built-in sensitive alias, across all groups.</summary>
    public static readonly string[] All = [.. Email, .. Password, .. Token, .. Card, .. Ssn, .. Phone];

    /// <summary>
    /// Builds an anchored, alternation pattern (<c>^(a|b|c)$</c>) for the given key aliases —
    /// ready to use as a <see cref="MaskingRuleConfig.Pattern"/>.
    /// </summary>
    public static string Pattern(params string[] keys) => $"^({string.Join('|', keys)})$";
}
