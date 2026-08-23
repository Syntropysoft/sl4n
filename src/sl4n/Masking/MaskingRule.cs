using System.Text.RegularExpressions;

namespace Sl4n;

/// <summary>
/// One masking rule: a field-NAME pattern plus how to redact the values under it. Rules are matched
/// against the key, never against the value's contents, and the first match wins.
/// </summary>
public sealed class MaskingRule
{
    private readonly Regex                _keyPattern;
    private readonly MaskingStrategy      _strategy;
    private readonly Func<string, string>? _customMask;

    /// <summary>Builds a rule.</summary>
    /// <param name="keyPattern">Matched against the field name. Prefer <see cref="MaskingPatterns"/>' source-generated regexes.</param>
    /// <param name="strategy">How to redact a matching value.</param>
    /// <param name="customMask">Required when <paramref name="strategy"/> is <see cref="MaskingStrategy.Custom"/>. It must not throw; if it does, the field is redacted fail-secure and <c>OnMaskingError</c> fires.</param>
    public MaskingRule(Regex keyPattern, MaskingStrategy strategy, Func<string, string>? customMask = null)
    {
        _keyPattern = keyPattern;
        _strategy   = strategy;
        _customMask = customMask;
    }

    /// <summary>True when this rule governs <paramref name="key"/>.</summary>
    public bool Matches(string key) => _keyPattern.IsMatch(key);

    /// <summary>Redacts <paramref name="value"/> according to this rule's strategy.</summary>
    public string Apply(string value) => _strategy switch
    {
        MaskingStrategy.Email    => MaskEmail(value),
        MaskingStrategy.FullMask => new string('*', value.Length),
        MaskingStrategy.LastFour => MaskLastFour(value),
        MaskingStrategy.Custom   => _customMask!(value),
        _                        => value
    };

    private static string MaskEmail(string value)
    {
        int atIndex = value.IndexOf('@');
        if (atIndex <= 1) return new string('*', value.Length);

        string local  = value[..atIndex];
        string domain = value[atIndex..];

        if (local.Length <= 2) return new string('*', local.Length) + domain;

        return local[0] + new string('*', local.Length - 2) + local[^1] + domain;
    }

    private static string MaskLastFour(string value)
    {
        if (value.Length <= 4) return new string('*', value.Length);
        return new string('*', value.Length - 4) + value[^4..];
    }
}
