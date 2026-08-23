using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Sl4n;

/// <summary>
/// The MEL provider that hands out sl4n loggers. Registered by <c>AddSl4n</c>; you rarely construct
/// it yourself. Loggers it creates do no work beyond snapshotting the scope and writing to the
/// channel — masking, filtering and serialization happen on the worker thread.
/// </summary>
public sealed class Sl4nLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ChannelWriter<RawLogEvent> _writer;
    private readonly Task                       _completion;
    private IExternalScopeProvider              _scopeProvider = new LoggerExternalScopeProvider();

    internal Sl4nLoggerProvider(Channel<RawLogEvent> channel)
    {
        _writer     = channel.Writer;
        _completion = channel.Reader.Completion;
    }

    /// <summary>Receives the shared scope provider from the logging infrastructure.</summary>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    /// <summary>Creates a logger for <paramref name="categoryName"/>.</summary>
    public ILogger CreateLogger(string categoryName) =>
        new Sl4nLogger(categoryName, _writer, _completion, _scopeProvider);

    /// <summary>Nothing to release: the channel and the worker are owned by the container.</summary>
    public void Dispose() { }
}
