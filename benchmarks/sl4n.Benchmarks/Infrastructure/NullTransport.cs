namespace Sl4n.Benchmarks;

/// <summary>Discards all entries — used to isolate the logger hot path from I/O.</summary>
internal sealed class NullTransport : ITransport
{
    public void Log(IReadOnlyDictionary<string, object?> entry) { }
}
