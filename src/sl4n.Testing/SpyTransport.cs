namespace Sl4n.Testing;

/// <summary>
/// An <see cref="ITransport"/> that captures every emitted entry in memory so tests can assert on
/// levels, messages, context fields and masking output — the .NET equivalent of SyntropyLog's
/// <c>SpyTransport</c>. Thread-safe (the worker emits from a background thread). Register it with
/// <c>services.UseSpyTransport(spy)</c>.
/// </summary>
public sealed class SpyTransport : ITransport
{
    private readonly object _gate = new();
    private readonly List<IReadOnlyDictionary<string, object?>> _entries = new();

    /// <inheritdoc />
    public void Log(IReadOnlyDictionary<string, object?> entry)
    {
        // The worker reuses the same dictionary instance across entries — copy defensively.
        Dictionary<string, object?> copy = new(entry);
        lock (_gate) _entries.Add(copy);
    }

    /// <summary>A point-in-time snapshot of all captured entries, in emit order.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    /// <summary>Number of captured entries.</summary>
    public int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>Discards all captured entries.</summary>
    public void Clear() { lock (_gate) _entries.Clear(); }

    /// <summary>Captured entries at the given level name (<c>information</c>, <c>error</c>, …).</summary>
    public IEnumerable<IReadOnlyDictionary<string, object?>> AtLevel(string level) =>
        Entries.Where(e => Field(e, "level") as string == level);

    /// <summary>Captured entries carrying a field <paramref name="key"/> equal to <paramref name="value"/>.</summary>
    public IEnumerable<IReadOnlyDictionary<string, object?>> WithField(string key, object? value) =>
        Entries.Where(e => Equals(Field(e, key), value));

    /// <summary>True if any captured entry at <paramref name="level"/> has a message containing <paramref name="substring"/>.</summary>
    public bool AnyMessageContains(string level, string substring) =>
        AtLevel(level).Any(e => (Field(e, "message") as string)?.Contains(substring) == true);

    private static object? Field(IReadOnlyDictionary<string, object?> entry, string key) =>
        entry.TryGetValue(key, out object? v) ? v : null;
}
