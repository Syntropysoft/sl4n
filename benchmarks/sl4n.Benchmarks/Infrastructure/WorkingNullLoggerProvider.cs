using Microsoft.Extensions.Logging;

namespace Sl4n.Benchmarks;

/// <summary>
/// MEL provider that does real structural work on every log call:
/// captures scope, formats message, iterates structured state, builds entry dict.
/// No masking, no channel, no I/O — result is discarded.
///
/// Honest baseline: same hot-path work as sl4n minus the scope-snapshot optimization
/// and minus masking. Shows the cost of the old "everything on the caller thread" approach.
/// </summary>
internal sealed class WorkingNullLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    public ILogger CreateLogger(string categoryName) =>
        new WorkingNullLogger(categoryName, _scopeProvider);

    public void Dispose() { }
}

internal sealed class WorkingNullLogger : ILogger
{
    private readonly string                _category;
    private readonly IExternalScopeProvider _scopeProvider;

    private static readonly Action<object?, Dictionary<string, object?>> _accumulateScope =
        static (scope, dict) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> kvScope)
                foreach (KeyValuePair<string, object?> kv in kvScope)
                    dict[kv.Key] = kv.Value;
            else if (scope is IEnumerable<KeyValuePair<string, string>> strScope)
                foreach (KeyValuePair<string, string> kv in strScope)
                    dict[kv.Key] = (object?)kv.Value;
        };

    internal WorkingNullLogger(string category, IExternalScopeProvider scopeProvider)
    {
        _category      = category;
        _scopeProvider = scopeProvider;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        _scopeProvider.Push(state);

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        Dictionary<string, object?> entry = new(8)
        {
            ["level"]    = logLevel.ToString().ToLowerInvariant(),
            ["category"] = _category,
            ["message"]  = formatter(state, exception)
        };

        _scopeProvider.ForEachScope(_accumulateScope, entry);

        if (state is IEnumerable<KeyValuePair<string, object?>> structured)
            foreach (KeyValuePair<string, object?> kv in structured)
            {
                if (kv.Key == "{OriginalFormat}") continue;
                entry[kv.Key] = kv.Value;
            }

        if (exception is not null)
            entry["exception"] = exception.ToString();

        _ = entry;
    }
}
