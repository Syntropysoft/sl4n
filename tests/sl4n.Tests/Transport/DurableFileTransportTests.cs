using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

public sealed class DurableFileTransportTests
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
        Path.Combine(Path.GetTempPath(), "sl4n-durable-" + Guid.NewGuid().ToString("N") + ".jsonl");

    private static void With(Action<string> body)
    {
        string path = TempPath();
        try { body(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void HappyPath_ForwardsDirectly_NoFileCreated() => With(path =>
    {
        FlakyTransport inner = new();
        DurableFileTransport durable = new(inner, path);

        durable.Log(Entry("a"));
        durable.Log(Entry("b"));

        inner.Delivered.Should().Equal("a", "b");
        File.Exists(path).Should().BeFalse(); // disk untouched on the happy path
    });

    [Fact]
    public void Outage_BuffersToDisk() => With(path =>
    {
        FlakyTransport inner = new() { Up = false };
        DurableFileTransport durable = new(inner, path);

        durable.Log(Entry("a"));
        durable.Log(Entry("b"));

        inner.Delivered.Should().BeEmpty();
        File.Exists(path).Should().BeTrue();
        File.ReadAllLines(path).Should().HaveCount(2);
    });

    [Fact]
    public void Recovery_DrainsInOrder_AndDeletesFile() => With(path =>
    {
        FlakyTransport inner = new() { Up = false };
        DurableFileTransport durable = new(inner, path);

        durable.Log(Entry("a"));   // buffered
        durable.Log(Entry("b"));   // buffered

        inner.Up = true;
        durable.Log(Entry("c"));   // triggers drain

        inner.Delivered.Should().Equal("a", "b", "c"); // order preserved
        File.Exists(path).Should().BeFalse();          // spool self-deleted after draining
    });

    [Fact]
    public void Restart_RecoversLeftoverSpool_OnConstruction() => With(path =>
    {
        // Simulate a spool left on disk by a crash during a prior outage.
        FlakyTransport down = new() { Up = false };
        DurableFileTransport crashed = new(down, path);
        crashed.Log(Entry("x"));
        crashed.Log(Entry("y"));
        File.Exists(path).Should().BeTrue();

        // A fresh instance (process restart) with a healthy sink flushes the leftover on construction.
        FlakyTransport recovered = new() { Up = true };
        _ = new DurableFileTransport(recovered, path);

        recovered.Delivered.Should().Equal("x", "y");
        File.Exists(path).Should().BeFalse();
    });

    [Fact]
    public void SinkStillDown_LeavesFileIntact() => With(path =>
    {
        FlakyTransport inner = new() { Up = false };
        DurableFileTransport durable = new(inner, path);

        durable.Log(Entry("a"));
        durable.Log(Entry("b"));
        durable.Log(Entry("c"));

        File.ReadAllLines(path).Should().HaveCount(3); // still buffering, nothing lost
        inner.Delivered.Should().BeEmpty();
    });
}
