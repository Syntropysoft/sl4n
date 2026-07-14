using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Sl4n;

/// <summary>
/// A durability <b>buffer</b> in front of an inner transport — not a log archive. In the happy path
/// entries go straight to the inner transport and nothing touches disk. When the inner transport
/// throws, entries are spooled to a file and retried on subsequent logs (and on restart); once the
/// backlog drains, the file is <b>deleted</b>. The spool only ever holds the undelivered backlog, so
/// it self-empties on recovery — no rotation, no archive, no cleanup to maintain.
/// </summary>
public sealed class DurableFileTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly string     _spoolPath;
    private readonly object     _gate = new();
    private bool                _hasBacklog;
    private bool                _corruptReported;

    private readonly Action<Exception>?         _onOutageStarted;
    private readonly Action<int>?               _onBacklogDrained;
    private readonly Action<Exception, string>? _onCorruptLine;

    /// <param name="inner">The real sink (e.g. an HTTP/DB transport) that may fail transiently.</param>
    /// <param name="spoolPath">File used to buffer the undelivered backlog during an outage.</param>
    /// <param name="onOutageStarted">Fired ONCE per outage — when a live delivery first fails and
    /// buffering begins — with the inner transport's exception. Buffering is the failure handling
    /// either way; this makes the outage (and its cause) observable instead of silent.</param>
    /// <param name="onBacklogDrained">Fired when the backlog fully drains (spool deleted), with the
    /// number of entries delivered by that drain.</param>
    /// <param name="onCorruptLine">Fired (once per outage episode) when an unparseable spool line —
    /// e.g. truncated by a crash mid-append — is skipped so the spool cannot stay wedged.</param>
    public DurableFileTransport(
        ITransport                  inner,
        string                      spoolPath,
        Action<Exception>?          onOutageStarted  = null,
        Action<int>?                onBacklogDrained = null,
        Action<Exception, string>?  onCorruptLine    = null)
    {
        _inner            = inner;
        _spoolPath        = spoolPath;
        _onOutageStarted  = onOutageStarted;
        _onBacklogDrained = onBacklogDrained;
        _onCorruptLine    = onCorruptLine;
        Recover();
    }

    /// <inheritdoc />
    public void Log(IReadOnlyDictionary<string, object?> entry)
    {
        lock (_gate)
        {
            if (!_hasBacklog)
            {
                if (TryForward(entry, out Exception? error)) return;   // happy path — disk untouched
                _hasBacklog = true;                                    // sink just went down → begin buffering
                _onOutageStarted?.Invoke(error!);                      // once per transition, not per entry
                Append(Serialize(entry));
                return;
            }

            Append(Serialize(entry));            // buffer the new entry (preserves order)
            Drain();                             // probe recovery; deletes the file when drained
        }
    }

    // On startup, flush any spool left over from a crash during a prior outage.
    private void Recover()
    {
        if (!File.Exists(_spoolPath)) return;
        _hasBacklog = true;
        Drain();
    }

    private bool TryForward(IReadOnlyDictionary<string, object?> entry, out Exception? error)
    {
        try { _inner.Log(entry); error = null; return true; }
        catch (Exception ex) { error = ex; return false; }   // buffering IS the failure handling — swallow and spool
    }

    // Delivers spooled entries in order, stopping at the first failure. Deletes the file when the
    // backlog fully drains; rewrites it with the remainder on partial progress; returns in O(1) when
    // the inner transport is still down (no progress → the file is left intact).
    private void Drain()
    {
        if (!File.Exists(_spoolPath)) { _hasBacklog = false; return; }

        int delivered = 0;
        List<string>? remainder = null;
        foreach (string line in File.ReadLines(_spoolPath))
        {
            if (remainder is not null) { remainder.Add(line); continue; }

            Dictionary<string, object?> entry;
            try
            {
                entry = Deserialize(line);
            }
            catch (Exception ex)
            {
                // A crash mid-append leaves a truncated line — it was never a complete entry.
                // Skipping (reported once per episode) is the only way the spool can't stay
                // wedged forever on the same poison line. It is dropped at the next rewrite/delete.
                if (!_corruptReported) { _corruptReported = true; _onCorruptLine?.Invoke(ex, line); }
                continue;
            }

            if (TryForward(entry, out _)) { delivered++; continue; }
            if (delivered == 0) return;                       // still down, no progress → file intact (O(1))
            remainder = new List<string> { line };            // partial progress → collect the rest
        }

        if (remainder is null)
        {
            File.Delete(_spoolPath);   // fully drained → the spool is gone until the next outage
            _hasBacklog = false;
            _corruptReported = false;
            if (delivered > 0) _onBacklogDrained?.Invoke(delivered);
        }
        else
        {
            File.WriteAllText(_spoolPath, string.Join('\n', remainder) + "\n");
        }
    }

    private void Append(string line)
    {
        string? dir = Path.GetDirectoryName(_spoolPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.AppendAllText(_spoolPath, line + "\n");
    }

    // ── Flat JSON-line (de)serialization — AOT-safe (manual writer + JsonDocument, no reflection) ──

    private static string Serialize(IReadOnlyDictionary<string, object?> entry)
    {
        ArrayBufferWriter<byte> buffer = new(256);
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> kv in entry)
            {
                writer.WritePropertyName(kv.Key);
                WriteValue(writer, kv.Value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:        writer.WriteNullValue();                   break;
            case bool b:      writer.WriteBooleanValue(b);               break;
            case int i:       writer.WriteNumberValue(i);                break;
            case long l:      writer.WriteNumberValue(l);                break;
            case double d:    writer.WriteNumberValue(d);                break;
            case float f:     writer.WriteNumberValue(f);                break;
            case decimal dec: writer.WriteNumberValue(dec);              break;
            case string s:    writer.WriteStringValue(s);                break;
            default:          writer.WriteStringValue(value.ToString()); break;
        }
    }

    private static Dictionary<string, object?> Deserialize(string line)
    {
        Dictionary<string, object?> entry = new();
        using JsonDocument doc = JsonDocument.Parse(line);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            entry[prop.Name] = ReadValue(prop.Value);
        return entry;
    }

    private static object? ReadValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out long l) ? l : el.GetDouble(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Null   => null,
        _                    => el.GetRawText(),
    };
}
