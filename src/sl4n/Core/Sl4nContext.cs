using System.Collections.Immutable;

namespace Sl4n;

public static class Sl4nContext
{
    private static readonly AsyncLocal<ImmutableDictionary<string, object?>> _store =
        new AsyncLocal<ImmutableDictionary<string, object?>>();

    public static ImmutableDictionary<string, object?> Current =>
        _store.Value ?? ImmutableDictionary<string, object?>.Empty;

    public static Sl4nScope Push(params (string Key, object? Value)[] fields)
    {
        ImmutableDictionary<string, object?> previous = Current;
        _store.Value = previous.SetItems(
            fields.Select(f => KeyValuePair.Create(f.Key, f.Value)));
        return new Sl4nScope(previous);
    }

    public static Sl4nScope Push(IEnumerable<KeyValuePair<string, string>> fields)
    {
        ImmutableDictionary<string, object?> previous = Current;
        _store.Value = previous.SetItems(
            fields.Select(f => KeyValuePair.Create(f.Key, (object?)f.Value)));
        return new Sl4nScope(previous);
    }

    public static void Set(string key, object? value) =>
        _store.Value = Current.SetItem(key, value);

    public static IReadOnlyDictionary<string, string> ExtractInbound(
        IReadOnlyDictionary<string, string> headers,
        string source,
        ContextConfig config)
    {
        if (!config.Inbound.TryGetValue(source, out Dictionary<string, string>? inboundMap))
            return ImmutableDictionary<string, string>.Empty;

        return inboundMap
            .Where(e => headers.ContainsKey(e.Value.ToLowerInvariant()))
            .ToImmutableDictionary(e => e.Key, e => headers[e.Value.ToLowerInvariant()]);
    }

    public static IReadOnlyDictionary<string, string> GetPropagationHeaders(
        string target,
        ContextConfig config)
    {
        ImmutableDictionary<string, object?> ctx = Current;
        if (ctx.IsEmpty) return ImmutableDictionary<string, string>.Empty;
        if (!config.Outbound.TryGetValue(target, out Dictionary<string, string>? targetMap))
            return ImmutableDictionary<string, string>.Empty;

        return targetMap
            .Where(e => ctx.ContainsKey(e.Key))
            .ToImmutableDictionary(e => e.Value, e => ctx[e.Key]!.ToString()!);
    }

    internal static void Restore(ImmutableDictionary<string, object?> previous) =>
        _store.Value = previous;
}
