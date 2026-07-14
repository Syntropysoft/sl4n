using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

/// <summary>
/// Observability of the durable buffer (ported lesson from SyntropyLog JS 1.4.1):
/// buffering IS the failure handling, but the outage — and its cause — must be
/// reportable, not silent. All callbacks are optional; absent, behavior is unchanged
/// (covered by DurableFileTransportTests).
/// </summary>
public sealed class DurableFileTransportCallbackTests
{
    private sealed class FlakyTransport : ITransport
    {
        public bool Up = true;
        public List<string> Delivered { get; } = new();

        public void Log(IReadOnlyDictionary<string, object?> entry)
        {
            if (!Up) throw new IOException("sink down");
            Delivered.Add(entry.TryGetValue("message", out object? m) ? m as string ?? "" : "");
        }
    }

    private static Dictionary<string, object?> Entry(string message) => new() { ["message"] = message };

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "sl4n-durable-cb-" + Guid.NewGuid().ToString("N") + ".jsonl");

    private static void With(Action<string> body)
    {
        string path = TempPath();
        try { body(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void OutageStarted_FiresOncePerOutage_WithTheInnerException() => With(path =>
    {
        List<Exception> outages = new();
        FlakyTransport inner = new() { Up = false };
        DurableFileTransport durable = new(inner, path, onOutageStarted: outages.Add);

        durable.Log(Entry("a"));
        durable.Log(Entry("b"));                       // same outage — no second report

        outages.Should().ContainSingle().Which.Should().BeOfType<IOException>()
            .Which.Message.Should().Be("sink down");

        inner.Up = true;
        durable.Log(Entry("c"));                       // drains, outage over
        inner.Up = false;
        durable.Log(Entry("d"));                       // NEW outage → second report

        outages.Should().HaveCount(2);
    });

    [Fact]
    public void BacklogDrained_ReportsTheDeliveredCount() => With(path =>
    {
        List<int> drains = new();
        FlakyTransport inner = new() { Up = false };
        DurableFileTransport durable = new(inner, path, onBacklogDrained: drains.Add);

        durable.Log(Entry("a"));
        durable.Log(Entry("b"));
        inner.Up = true;
        durable.Log(Entry("c"));                       // c is buffered then the whole spool drains

        drains.Should().Equal(3);
        File.Exists(path).Should().BeFalse();
    });

    [Fact]
    public void CorruptSpoolLine_IsSkippedAndReported_SpoolNotWedged() => With(path =>
    {
        // A crash mid-append leaves a truncated line. Before the fix this threw inside
        // Drain() on EVERY subsequent Log(), wedging the spool forever.
        File.WriteAllText(path, "{\"message\":\"ok-1\"}\n{\"message\":\"tru\n{\"message\":\"ok-2\"}\n");

        List<string> corrupt = new();
        FlakyTransport inner = new();                  // healthy sink
        DurableFileTransport durable = new(inner, path,
            onCorruptLine: (_, line) => corrupt.Add(line));

        // Recover() ran in the constructor: good lines delivered, poison skipped + reported.
        inner.Delivered.Should().Equal("ok-1", "ok-2");
        corrupt.Should().ContainSingle().Which.Should().StartWith("{\"message\":\"tru");
        File.Exists(path).Should().BeFalse();          // spool cleared, not wedged

        durable.Log(Entry("after"));                   // happy path restored
        inner.Delivered.Should().Contain("after");
    });
}
