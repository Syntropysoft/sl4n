using System.Text;

namespace Sl4n;

/// <summary>
/// Human-readable single-line console transport for local development:
/// <c>{timestamp} [LVL] category: message key=value …</c>. For machine-ingestible output use the
/// default JSON <see cref="ConsoleTransport"/>. Swap it in with <c>services.UseClassicConsole()</c>.
/// </summary>
public sealed class ClassicConsoleTransport : ITransport
{
    public void Log(IReadOnlyDictionary<string, object?> entry)
    {
        entry.TryGetValue("timestamp", out object? ts);
        entry.TryGetValue("level",     out object? level);
        entry.TryGetValue("category",  out object? category);
        entry.TryGetValue("message",   out object? message);

        StringBuilder sb = new(128);
        if (ts is not null) sb.Append(ts).Append(' ');
        sb.Append('[').Append(ShortLevel(level as string)).Append("] ");
        if (category is not null) sb.Append(category).Append(": ");
        sb.Append(message);

        foreach (KeyValuePair<string, object?> kv in entry)
        {
            if (kv.Key is "timestamp" or "level" or "category" or "message") continue;
            sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
        }

        Console.WriteLine(sb.ToString());
    }

    private static string ShortLevel(string? level) => level switch
    {
        "trace"       => "TRC",
        "debug"       => "DBG",
        "information" => "INF",
        "warning"     => "WRN",
        "error"       => "ERR",
        "critical"    => "CRT",
        _             => (level ?? "LOG").ToUpperInvariant(),
    };
}
