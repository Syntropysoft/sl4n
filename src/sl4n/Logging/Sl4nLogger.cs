using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Sl4n;

internal sealed class Sl4nLogger : ILogger
{
    private readonly string                      _categoryName;
    private readonly ChannelWriter<RawLogEvent>  _writer;
    private readonly Task                        _completion;
    private readonly IExternalScopeProvider      _scopeProvider;

    // Cached static delegate — allocated once, never per-call
    private static readonly Action<object?, List<KeyValuePair<string, object?>>> _collectScope =
        static (scope, list) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> kvScope)
                foreach (KeyValuePair<string, object?> kv in kvScope)
                    list.Add(kv);
            else if (scope is IEnumerable<KeyValuePair<string, string>> strScope)
                foreach (KeyValuePair<string, string> kv in strScope)
                    list.Add(KeyValuePair.Create(kv.Key, (object?)kv.Value));
        };

    internal Sl4nLogger(
        string                     categoryName,
        ChannelWriter<RawLogEvent> writer,
        Task                       completion,
        IExternalScopeProvider     scopeProvider)
    {
        _categoryName  = categoryName;
        _writer        = writer;
        _completion    = completion;
        _scopeProvider = scopeProvider;
    }

    // Channel closed (worker stopped) → MEL skips everything, zero allocation.
    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && !_completion.IsCompleted;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _scopeProvider.Push(state);

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        // Snapshot scope now — AsyncLocal values are invisible to the background worker.
        List<KeyValuePair<string, object?>> scopeList = new(4);
        _scopeProvider.ForEachScope(_collectScope, scopeList);

        _writer.TryWrite(new RawLogEvent(
            logLevel,
            _categoryName,
            formatter(state, exception),
            state as IEnumerable<KeyValuePair<string, object?>>,
            exception,
            scopeList.Count > 0 ? scopeList : null));
    }
}
