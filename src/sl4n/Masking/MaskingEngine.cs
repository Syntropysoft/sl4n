using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Sl4n;

public sealed class MaskingEngine
{
    /// <summary>Value substituted for a non-string under a sensitive key, or a masking failure.</summary>
    internal const string Redacted = "[REDACTED]";

    /// <summary>
    /// Cap for the per-key-name decision cache. Bounded so attacker-controlled key names
    /// (a hostile payload generating unique keys per log) cannot grow memory: past the cap,
    /// new keys still mask correctly — they just pay the rule scan, uncached.
    /// </summary>
    internal const int DecisionCacheMax = 4096;

    private readonly IReadOnlyList<MaskingRule>     _rules;
    private readonly Action<Exception, string>?     _onMaskingError;

    // Per-key-name decision cache: key → matched rule (or null for "no rule matched").
    // Field NAMES repeat across log entries while values change, yet every key paid the
    // full rule scan per log — and custom config rules are runtime-interpreted regexes.
    // Caching the DECISION (never the value) removes the scan from the hot path. Family
    // fix (found by the Java port's JMH suite; landed in JS and Python the same day).
    // Deterministic by design: the decision depends only on the key name and the rule
    // set, which is immutable after construction — so no invalidation is ever needed.
    private readonly ConcurrentDictionary<string, MaskingRule?> _decisionCache = new();

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

    /// <summary>
    /// True when a masking rule applies to this key name — served from the decision cache,
    /// so callers (e.g. the worker deciding whether to re-render the message from masked
    /// values) pay a dictionary hit. A regex timeout answers <c>true</c>: fail-secure,
    /// assume sensitive.
    /// </summary>
    public bool HasRuleFor(string key)
    {
        try
        {
            return FindMatchingRule(key) is not null;
        }
        catch (RegexMatchTimeoutException ex)
        {
            _onMaskingError?.Invoke(ex, key);
            return true; // fail-secure; transient → not cached
        }
    }

    /// <summary>
    /// Masks one value by its key — the per-pair form of <see cref="Apply"/>, for a caller that
    /// needs the raw and the masked value out of a SINGLE pass over the state (the worker building
    /// a masked and an unmasked projection at once). Same decision, same cache, same fail-secure
    /// behaviour; equivalence with <see cref="Apply"/> is pinned by test.
    /// </summary>
    internal object? MaskOne(string key, object? value) =>
        _rules.Count == 0 ? value : MaskValue(key, value);

    private object? MaskValue(string key, object? value)
    {
        if (value is null) return null;

        MaskingRule? rule;
        try
        {
            rule = FindMatchingRule(key);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _onMaskingError?.Invoke(ex, key);
            return Redacted; // fail-secure: couldn't evaluate the rule → redact
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

    /// <summary>
    /// First rule whose key-pattern matches (order preserved → first wins), served from the
    /// bounded decision cache when the key name has been seen before. A ReDoS timeout during
    /// the scan propagates WITHOUT being cached — a transient failure must not poison the
    /// decision for a key that would normally evaluate fine.
    /// </summary>
    private MaskingRule? FindMatchingRule(string key)
    {
        if (_decisionCache.TryGetValue(key, out MaskingRule? cached))
            return cached; // null here means cached "no rule matched"

        MaskingRule? found = ScanRules(key); // may throw RegexMatchTimeoutException — bypasses the cache
        if (_decisionCache.Count < DecisionCacheMax)
            _decisionCache.TryAdd(key, found);

        return found;
    }

    /// <summary>The uncached scan: every rule, in order. Depends only on key + rule set.</summary>
    private MaskingRule? ScanRules(string key)
    {
        for (int i = 0; i < _rules.Count; i++)
        {
            if (_rules[i].Matches(key)) return _rules[i];
        }
        return null;
    }

    /// <summary>Entries currently held by the decision cache (test hook).</summary>
    internal int DecisionCacheSize => _decisionCache.Count;
}
