namespace Sl4n;

public sealed class MaskingEngine
{
    private readonly IReadOnlyList<MaskingRule> _rules;

    public MaskingEngine(IReadOnlyList<MaskingRule> rules)
    {
        _rules = rules;
    }

    public static MaskingEngine Create(MaskingConfig config)
    {
        IReadOnlyList<MaskingRule> rules = config.EnableDefaultRules
            ? DefaultRules.Build()
            : [];
        return new MaskingEngine(rules);
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
            if (rule.Matches(key)) return rule.Apply(value.ToString() ?? string.Empty);
        }

        return value;
    }
}
