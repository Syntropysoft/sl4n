using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sl4n;

public sealed class Sl4nTransportWorker : IHostedService, IAsyncDisposable
{
    private readonly ChannelReader<RawLogEvent>  _reader;
    private readonly IReadOnlyList<ITransport>   _transports;
    private readonly MaskingEngine               _masking;
    private readonly CancellationTokenSource     _cts         = new();
    private Task                                 _executeTask = Task.CompletedTask;

    // Reused across every log entry — safe because SingleReader channel + synchronous transport.
    // Transport.Log() must not hold a reference to the dictionary after returning.
    private readonly Dictionary<string, object?> _dict = new(16);

    internal Sl4nTransportWorker(
        ChannelReader<RawLogEvent> reader,
        IEnumerable<ITransport>    transports,
        MaskingEngine              masking)
    {
        _reader     = reader;
        _transports = transports.ToList();
        _masking    = masking;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executeTask = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        try { await _executeTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _executeTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (RawLogEvent entry in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            Build(in entry);
            foreach (ITransport transport in _transports)
                transport.Log(_dict);
            _dict.Clear();
        }
    }

    private void Build(in RawLogEvent e)
    {
        _dict["level"]    = LevelName(e.Level);
        _dict["category"] = e.Category;
        _dict["message"]  = e.Message;

        // Scope fields are unmasked — they come from the propagation context, not from user log calls.
        if (e.ScopeFields is not null)
            foreach (KeyValuePair<string, object?> kv in e.ScopeFields)
                _dict[kv.Key] = kv.Value;

        if (e.StructuredState is not null)
            foreach (KeyValuePair<string, object?> kv in _masking.Apply(e.StructuredState))
            {
                if (kv.Key == "{OriginalFormat}") continue;
                _dict[kv.Key] = kv.Value;
            }

        if (e.Exception is not null)
            _dict["exception"] = e.Exception.ToString();
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace       => "trace",
        LogLevel.Debug       => "debug",
        LogLevel.Information => "information",
        LogLevel.Warning     => "warning",
        LogLevel.Error       => "error",
        LogLevel.Critical    => "critical",
        _                    => level.ToString().ToLowerInvariant()
    };
}
