using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sl4n.Benchmarks;

/// <summary>No-op MEL provider — baseline for allocation comparison.</summary>
internal sealed class NullLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
    public void Dispose() { }
}
