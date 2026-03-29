using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Sl4n;

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

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    public ILogger CreateLogger(string categoryName) =>
        new Sl4nLogger(categoryName, _writer, _completion, _scopeProvider);

    public void Dispose() { }
}
