using System.Text.RegularExpressions;

namespace Sl4n;

public sealed class MaskingEngine
{
    /// <summary>Value substituted for a non-string under a sensitive key, or a masking failure.</summary>
    internal const string Redacted = "[REDACTED]";

    private readonly IReadOnlyList<MaskingRule>     _rules;
    private readonly Action<Exception, string>?     _onMaskingError;

    public MaskingEngine(IReadOnlyList<MaskingRule> rules, Action<Exception, string>? onMaskingError = null)
    {
        _rules          = rules;
        _onMaskingError = onMaskingError;
    }

    public static MaskingEngine Create(MaskingConfig config)
    {
        List<MaskingRule> rules = new();

        if (config.EnableDefaultRules)
            rules.AddRange(DefaultRules.Build());

        TimeSpan timeout = config.RegexTimeoutMs > 0
            ? TimeSpan.FromMilliseconds(config.RegexTimeoutMs)
            : Regex.InfiniteMatchTimeout;

        foreach (MaskingRuleConfig rc in config.Rules)
        {
            if (string.IsNullOrWhiteSpace(rc.Pattern)) continue;

            // Custom cannot carry a function from config → fail-safe to FullMask (over-mask).
            MaskingStrategy strategy = rc.Strategy == MaskingStrategy.Custom
                ? MaskingStrategy.FullMask
                : rc.Strategy;

            // Runtime regex (no RegexOptions.Compiled → stays AOT-safe); timeout guards ReDoS.
            Regex pattern = new(rc.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout);
            rules.Add(new MaskingRule(pattern, strategy));
        }

        return new MaskingEngine(rules, config.OnMaskingError);
    }

    // Returns the input sequence unmodified when there are no rules (zero allocation).
    // When rules exist, projects lazily — no intermediate List<T>.
    public IEnumerable<KeyValuePair<string, object?>> Apply(
        IEnumerable<KeyValuePair<string, object?>> state)
    {
        if (_rules.Count == 0) return state;
        return state.Select(kv => KeyValuePair.Create(kv.Key, MaskValue(kv.Key, kv.Value)));
    }

    private object? MaskValue(string key, object? value)
    {
        if (value is null) return null;

        for (int i = 0; i < _rules.Count; i++)
        {
            MaskingRule rule = _rules[i];

            bool matches;
            try
            {
                matches = rule.Matches(key);
            }
            catch (RegexMatchTimeoutException ex)
            {
                _onMaskingError?.Invoke(ex, key);
                return Redacted; // fail-secure: couldn't evaluate the rule → redact
            }

            if (!matches) continue;

            // A non-string value under a sensitive key is fully redacted — never stringify-then-mask,
            // so an object/array/number under a sensitive-named field can't leak its contents.
            if (value is not string s) return Redacted;

            try
            {
                return rule.Apply(s);
            }
            catch (Exception ex)
            {
                _onMaskingError?.Invoke(ex, key);
                return Redacted; // masking never throws — logging keeps working
            }
        }

        return value;
    }
}
