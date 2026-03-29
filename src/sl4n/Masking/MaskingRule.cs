using System.Text.RegularExpressions;

namespace Sl4n;

public sealed class MaskingRule
{
    private readonly Regex                _keyPattern;
    private readonly MaskingStrategy      _strategy;
    private readonly Func<string, string>? _customMask;

    public MaskingRule(Regex keyPattern, MaskingStrategy strategy, Func<string, string>? customMask = null)
    {
        _keyPattern = keyPattern;
        _strategy   = strategy;
        _customMask = customMask;
    }

    public bool Matches(string key) => _keyPattern.IsMatch(key);

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
