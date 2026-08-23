using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Sl4n;

/// <summary>
/// The default transport: one JSON object per line on stdout, written with <c>Utf8JsonWriter</c> —
/// no reflection, no serializer configuration, AOT-safe. This is what a log shipper expects. For
/// human-readable local output see <see cref="ClassicConsoleTransport"/>.
/// </summary>
public sealed class ConsoleTransport : ITransport
{
    /// <summary>Writes the entry as a single JSON line.</summary>
    public void Log(IReadOnlyDictionary<string, object?> entry)
    {
        ArrayBufferWriter<byte> buffer = new(256);
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> kv in entry)
        {
            writer.WritePropertyName(kv.Key);
            WriteValue(writer, kv.Value);
        }
        writer.WriteEndObject();
        writer.Flush();
        Console.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
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
}
