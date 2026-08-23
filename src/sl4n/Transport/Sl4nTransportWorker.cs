using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sl4n;

public sealed class Sl4nTransportWorker : IHostedService, IAsyncDisposable
{
    private readonly ChannelReader<RawLogEvent>  _reader;
    private readonly IReadOnlyList<ITransport>   _transports;
    private readonly IReadOnlyList<ITransport>   _unmasked;
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

    // Second projection, carrying the values as they arrived. Allocated only when a sink is
    // registered under Sl4nTransportKeys.Unmasked — null is the common case and costs nothing.
    private readonly Dictionary<string, object?>? _rawDict;

    internal Sl4nTransportWorker(
        ChannelReader<RawLogEvent>  reader,
        IEnumerable<ITransport>     transports,
        MaskingEngine               masking,
        LoggingMatrix?              matrix       = null,
        Sl4nStats?                  stats        = null,
        Action<Exception, string>?  onLogFailure = null,
        RetentionRegistry?          retention    = null,
        IEnumerable<ITransport>?    unmasked     = null)
    {
        _reader       = reader;
        _transports   = transports.ToList();
        _unmasked     = unmasked?.ToList() ?? [];
        _rawDict      = _unmasked.Count > 0 ? new Dictionary<string, object?>(16) : null;
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
        // Idempotent: the worker is registered both as a singleton and as its own
        // IHostedService (two descriptors, one instance), so the ServiceProvider disposes
        // it twice — the second pass must not touch the already-disposed CTS. Found by
        // the AOT smoke: a clean host shutdown crashed with ObjectDisposedException.
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { await _executeTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    private bool _disposed;

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
                _rawDict?.Clear();
                continue;
            }

            Deliver(_transports, _dict);
            if (_rawDict is not null) Deliver(_unmasked, _rawDict);

            _dict.Clear();
            _rawDict?.Clear();
        }
    }

    // Per-transport isolation — one transport throwing must not kill the worker or starve the
    // others. Count it, surface it via OnLogFailure, and keep going.
    private void Deliver(IReadOnlyList<ITransport> transports, Dictionary<string, object?> entry)
    {
        foreach (ITransport transport in transports)
        {
            try
            {
                transport.Log(entry);
            }
            catch (Exception ex)
            {
                _stats.IncrTransportFailures();
                _onLogFailure?.Invoke(ex, transport.GetType().Name);
            }
        }
    }

    private void Build(in RawLogEvent e)
    {
        string level = LevelName(e.Level);
        if (e.Timestamp != default) Put("timestamp", e.Timestamp.ToString("O"));
        Put("level",    level);
        Put("category", e.Category);
        // MEL's own message, interpolated with the RAW values. The masked projection overwrites it
        // below when a key is maskable (7.5); the unmasked one keeps it — that is the whole point.
        Put("message",  Sanitizer.Clean(e.Message));

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
                    Put(kv.Key, Sanitize(kv.Value));
            }
        }

        // Retention metadata bypasses the matrix — it is a compliance tag, not user context.
        if (retentionName is not null)
        {
            Put("retention", retentionName);
            if (_retention.TryResolve(retentionName, out RetentionPolicy? policy))
            {
                Put("retentionClass", policy.Class);
                Put("retentionDays",  policy.Days);
            }
        }

        if (e.StructuredState is not null)
        {
            string? originalFormat = null;
            bool maskable = false;
            // ONE pass over the state. It is a lazy reference that can hang off a request scope
            // (see the ObjectDisposedException catch above), so masking per key with MaskOne —
            // rather than projecting twice through Apply — is what keeps the second projection
            // from re-enumerating it. MaskOne is Apply pair-for-pair; equivalence is pinned by test.
            foreach (KeyValuePair<string, object?> kv in e.StructuredState)
            {
                if (kv.Key == "{OriginalFormat}") { originalFormat = kv.Value as string; continue; }
                if (_rawDict is not null) _rawDict[kv.Key] = Sanitize(kv.Value);
                _dict[kv.Key] = Sanitize(_masking.MaskOne(kv.Key, kv.Value));
                if (!maskable && _masking.HasRuleFor(kv.Key)) maskable = true;
            }

            // MEL pre-formats the message with RAW values (Sl4nLogger stores formatter(state)),
            // so a template like "charged {Email}" would leak in `message` the very value the
            // Email field masks. When any state key is maskable, re-render the message from the
            // MASKED values — the README quick start is the contract. Re-rendering loses custom
            // format specifiers on these entries (honesty over fidelity); entries with no
            // maskable key keep MEL's exact formatting, byte for byte.
            if (maskable && originalFormat is not null)
                _dict["message"] = Sanitizer.Clean(RenderTemplate(originalFormat, _dict));
        }

        // Exception blob is left intact — its newlines carry the stack trace.
        if (e.Exception is not null)
            Put("exception", e.Exception.ToString());
    }

    // Writes to both projections. The unmasked one only exists when a sink is registered under
    // Sl4nTransportKeys.Unmasked; otherwise this is a plain dictionary write.
    private void Put(string key, object? value)
    {
        _dict[key] = value;
        if (_rawDict is not null) _rawDict[key] = value;
    }

    // Re-renders a MEL message template using the (already masked + sanitized) dict values.
    // AOT-safe token substitution: "{Name[,align][:format]}" → dict["Name"], "{{"/"}}" are
    // literal braces (MEL escaping), unknown tokens are left verbatim, null renders "(null)".
    private static string RenderTemplate(string format, IReadOnlyDictionary<string, object?> values)
    {
        StringBuilder sb = new(format.Length + 16);
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '{')
            {
                if (i + 1 < format.Length && format[i + 1] == '{') { sb.Append('{'); i++; continue; }
                int end = format.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(format, i, format.Length - i); break; }
                string token = format.Substring(i + 1, end - i - 1);
                int cut = token.IndexOfAny([',', ':']);
                string name = cut < 0 ? token : token[..cut];
                if (values.TryGetValue(name, out object? v))
                    sb.Append(v switch
                    {
                        null => "(null)",                // MEL renders null as "(null)"
                        // Invariant, like MEL's template formatter — a masked message must not
                        // change shape with the server's locale (299.9 vs "299,9").
                        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                        _ => v.ToString(),
                    });
                else
                    sb.Append(format, i, end - i + 1);   // unknown token → verbatim
                i = end;
            }
            else if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                sb.Append('}'); i++;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
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
