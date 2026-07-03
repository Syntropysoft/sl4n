using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sl4n;

public sealed class Sl4nTransportWorker : IHostedService, IAsyncDisposable
{
    private readonly ChannelReader<RawLogEvent>  _reader;
    private readonly IReadOnlyList<ITransport>   _transports;
    private readonly MaskingEngine               _masking;
    private readonly LoggingMatrix               _matrix;
    private readonly RetentionRegistry           _retention;
    private readonly Sl4nStats                    _stats;
    private readonly Action<Exception, string>?  _onLogFailure;
    private readonly CancellationTokenSource     _cts         = new();
    private Task                                 _executeTask = Task.CompletedTask;

    // Reused across every log entry — safe because SingleReader channel + synchronous transport.
    // Transport.Log() must not hold a reference to the dictionary after returning.
    private readonly Dictionary<string, object?> _dict = new(16);

    internal Sl4nTransportWorker(
        ChannelReader<RawLogEvent>  reader,
        IEnumerable<ITransport>     transports,
        MaskingEngine               masking,
        LoggingMatrix?              matrix       = null,
        Sl4nStats?                  stats        = null,
        Action<Exception, string>?  onLogFailure = null,
        RetentionRegistry?          retention    = null)
    {
        _reader       = reader;
        _transports   = transports.ToList();
        _masking      = masking;
        _matrix       = matrix ?? LoggingMatrix.Empty;
        _retention    = retention ?? RetentionRegistry.Empty;
        _stats        = stats ?? new Sl4nStats();
        _onLogFailure = onLogFailure;
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
            _stats.IncrLogsProcessed();

            try
            {
                Build(in entry);
            }
            catch (ObjectDisposedException)
            {
                // ASP.NET Hosting logs carry lazy IEnumerable<KVP> references to HttpContext.
                // If the worker processes them after the request completes, the context is disposed.
                // Safe to skip — the entry is a framework diagnostic, not user data.
                _stats.IncrDroppedEntries();
                _dict.Clear();
                continue;
            }

            // Per-transport isolation — one transport throwing must not kill the worker or starve
            // the others. Count it, surface it via OnLogFailure, and keep going.
            foreach (ITransport transport in _transports)
            {
                try
                {
                    transport.Log(_dict);
                }
                catch (Exception ex)
                {
                    _stats.IncrTransportFailures();
                    _onLogFailure?.Invoke(ex, transport.GetType().Name);
                }
            }

            _dict.Clear();
        }
    }

    private void Build(in RawLogEvent e)
    {
        string level = LevelName(e.Level);
        if (e.Timestamp != default) _dict["timestamp"] = e.Timestamp.ToString("O");
        _dict["level"]    = level;
        _dict["category"] = e.Category;
        _dict["message"]  = Sanitizer.Clean(e.Message);

        // Scope (context) fields are unmasked — they come from the propagation context, not from
        // user log calls — but they ARE filtered by the Logging Matrix for this level.
        // allowed == null means "allow all" (no matrix, or level maps to "*").
        string? retentionName = null;
        if (e.ScopeFields is not null)
        {
            HashSet<string>? allowed = _matrix.AllowedFields(level);
            foreach (KeyValuePair<string, object?> kv in e.ScopeFields)
            {
                if (kv.Key == Sl4nRetention.Field)          // structural tag — consumed, never emitted raw
                {
                    retentionName = kv.Value?.ToString();
                    continue;
                }
                if (allowed is null || allowed.Contains(kv.Key))
                    _dict[kv.Key] = Sanitize(kv.Value);
            }
        }

        // Retention metadata bypasses the matrix — it is a compliance tag, not user context.
        if (retentionName is not null)
        {
            _dict["retention"] = retentionName;
            if (_retention.TryResolve(retentionName, out RetentionPolicy? policy))
            {
                _dict["retentionClass"] = policy.Class;
                _dict["retentionDays"]  = policy.Days;
            }
        }

        if (e.StructuredState is not null)
            foreach (KeyValuePair<string, object?> kv in _masking.Apply(e.StructuredState))
            {
                if (kv.Key == "{OriginalFormat}") continue;
                _dict[kv.Key] = Sanitize(kv.Value);
            }

        // Exception blob is left intact — its newlines carry the stack trace.
        if (e.Exception is not null)
            _dict["exception"] = e.Exception.ToString();
    }

    // Strips control chars / ANSI from string values; non-strings pass through untouched.
    private static object? Sanitize(object? value) =>
        value is string s ? Sanitizer.Clean(s) : value;

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
