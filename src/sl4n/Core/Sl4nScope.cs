using System.Collections.Immutable;

namespace Sl4n;

public readonly struct Sl4nScope : IDisposable
{
    private readonly ImmutableDictionary<string, object?> _previous;

    internal Sl4nScope(ImmutableDictionary<string, object?> previous)
    {
        _previous = previous;
    }

    public void Dispose() => Sl4nContext.Restore(_previous);
}
