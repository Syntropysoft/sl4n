using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Sl4n;

public sealed class MaskingEngine
{
    /// <summary>Value substituted for a non-string under a sensitive key, or a masking failure.</summary>
    internal const string Redacted = "[REDACTED]";

    /// <summary>Past this many distinct key names, new keys still mask correctly — they just pay the scan uncached.</summary>
    private const int DecisionCacheCap = 4096;

    private readonly IReadOnlyList<MaskingRule>     _rules;
    private readonly Action<Exception, string>?     _onMaskingError;

    // Field NAMES repeat across log entries while values change, so the decision
    // (key name → matched rule, or none) is cached — never the value. The rule set is
    // immutable after Create(), so no invalidation is needed. Concurrent because the
    // engine is a shared DI singleton. (Family fix: SyntropyLog JS 1.4.0, 2.4× masking.)
    private readonly ConcurrentDictionary<string, MaskingRule?> _decisions = new();

    public MaskingEngine(IReadOnlyList<MaskingRule> rules, Action<Exception, string>? onMaskingError = null)
    {
        _rules          = rules;
        _onMaskingError = onMaskingError;
    }

    public static MaskingEngine Create(MaskingConfig config, Action<Exception, string>? onMaskingError = null)
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

        return new MaskingEngine(rules, onMaskingError ?? config.OnMaskingError);
    }

    // Returns the input sequence unmodified when there are no rules (zero allocation).
    // When rules exist, projects lazily — no intermediate List<T>.
    public IEnumerable<KeyValuePair<string, object?>> Apply(
        IEnumerable<KeyValuePair<string, object?>> state)
    {
        if (_rules.Count == 0) return state;
        return state.Select(kv => KeyValuePair.Create(kv.Key, MaskValue(kv.Key, kv.Value)));
    }

    /// <summary>Distinct key names whose rule decision is currently cached (diagnostics).</summary>
    public int DecisionCacheSize => _decisions.Count;

    /// <summary>
    /// True when a masking rule applies to this key name — a cached decision, so callers
    /// (e.g. the worker deciding whether to re-render the message) pay a dictionary hit.
    /// A regex timeout answers <c>true</c>: fail-secure, assume sensitive.
    /// </summary>
    public bool HasRuleFor(string key)
    {
        if (_decisions.TryGetValue(key, out MaskingRule? rule)) return rule is not null;
        try
        {
            rule = Resolve(key);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _onMaskingError?.Invoke(ex, key);
            return true; // fail-secure; transient → not cached
        }
        if (_decisions.Count < DecisionCacheCap) _decisions[key] = rule;
        return rule is not null;
    }

    private object? MaskValue(string key, object? value)
    {
        if (value is null) return null;

        if (!_decisions.TryGetValue(key, out MaskingRule? rule))
        {
            try
            {
                rule = Resolve(key);
            }
            catch (RegexMatchTimeoutException ex)
            {
                _onMaskingError?.Invoke(ex, key);
                return Redacted; // fail-secure: couldn't evaluate the rule → redact. Transient → NOT cached.
            }
            if (_decisions.Count < DecisionCacheCap) _decisions[key] = rule;
        }

        if (rule is null) return value;

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

    // First matching rule wins — same order the rules were configured in.
    private MaskingRule? Resolve(string key)
    {
        for (int i = 0; i < _rules.Count; i++)
            if (_rules[i].Matches(key)) return _rules[i];
        return null;
    }
}
