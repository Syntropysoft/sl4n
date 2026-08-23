using System.Collections.Immutable;

namespace Sl4n;

/// <summary>
/// The lifetime of a set of context fields. Disposing it restores the context to what it was
/// before the matching <see cref="Sl4nContext.Push(ValueTuple{string, object}[])"/>.
/// </summary>
public readonly struct Sl4nScope : IDisposable
{
    private readonly ImmutableDictionary<string, object?> _previous;

    internal Sl4nScope(ImmutableDictionary<string, object?> previous)
    {
        _previous = previous;
    }

    /// <summary>Restores the context captured when this scope was opened.</summary>
    public void Dispose() => Sl4nContext.Restore(_previous);
}
