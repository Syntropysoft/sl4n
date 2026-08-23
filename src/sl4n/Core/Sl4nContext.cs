using System.Collections.Immutable;

namespace Sl4n;

/// <summary>
/// The ambient context that travels with a logical operation. Backed by <c>AsyncLocal</c>, so it
/// follows async continuations across threads without being passed through method signatures, and
/// every log emitted inside the scope carries it.
/// </summary>
public static class Sl4nContext
{
    private static readonly AsyncLocal<ImmutableDictionary<string, object?>> _store =
        new AsyncLocal<ImmutableDictionary<string, object?>>();

    /// <summary>The fields in force right now; empty when nothing has been pushed.</summary>
    public static ImmutableDictionary<string, object?> Current =>
        _store.Value ?? ImmutableDictionary<string, object?>.Empty;

    /// <summary>
    /// Adds fields for the lifetime of the returned scope. Dispose it — usually with
    /// <c>using</c> — to restore what was in force before.
    /// </summary>
    public static Sl4nScope Push(params (string Key, object? Value)[] fields)
    {
        ImmutableDictionary<string, object?> previous = Current;
        _store.Value = previous.SetItems(
            fields.Select(f => KeyValuePair.Create(f.Key, f.Value)));
        return new Sl4nScope(previous);
    }

    /// <summary>
    /// Adds string fields for the lifetime of the returned scope — the shape that comes out of
    /// <see cref="ExtractInbound"/>.
    /// </summary>
    public static Sl4nScope Push(IEnumerable<KeyValuePair<string, string>> fields)
    {
        ImmutableDictionary<string, object?> previous = Current;
        _store.Value = previous.SetItems(
            fields.Select(f => KeyValuePair.Create(f.Key, (object?)f.Value)));
        return new Sl4nScope(previous);
    }

    /// <summary>
    /// Sets one field with no scope to close, so it stays for the rest of the async flow. Prefer
    /// <see cref="Push(ValueTuple{string, object}[])"/> when the field has a natural lifetime.
    /// </summary>
    public static void Set(string key, object? value) =>
        _store.Value = Current.SetItem(key, value);

    /// <summary>
    /// Reads conceptual fields out of inbound headers using the map declared for
    /// <paramref name="source"/>. Headers with no mapping are ignored; an unknown source yields an
    /// empty result rather than an error.
    /// </summary>
    /// <param name="headers">Inbound headers, keyed lowercase.</param>
    /// <param name="source">Which <see cref="ContextConfig.Inbound"/> map to apply.</param>
    /// <param name="config">The context configuration.</param>
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

    /// <summary>
    /// The headers to attach to an outgoing call so the context survives the process boundary,
    /// named as <paramref name="target"/> declares. Only fields present in the current context are
    /// returned; an unknown target yields an empty result.
    /// </summary>
    /// <param name="target">Which <see cref="ContextConfig.Outbound"/> map to apply, e.g. <c>"http"</c>.</param>
    /// <param name="config">The context configuration.</param>
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
