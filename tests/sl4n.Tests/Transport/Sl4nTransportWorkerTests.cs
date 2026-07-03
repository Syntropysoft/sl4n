using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Xunit;

namespace Sl4n.Tests;

public sealed class Sl4nTransportWorkerTests
{
    private sealed class CapturingTransport : ITransport
    {
        public List<Dictionary<string, object?>> Entries { get; } = new();
        // Copy the dict — worker reuses the same instance across entries
        public void Log(IReadOnlyDictionary<string, object?> entry) =>
            Entries.Add(new Dictionary<string, object?>(entry));
    }

    private static Channel<RawLogEvent> UnboundedChannel() =>
        Channel.CreateUnbounded<RawLogEvent>();

    private static MaskingEngine NoOpMasking() =>
        MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = false });

    private static MaskingEngine DefaultMasking() =>
        MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

    private static ObjectPool<List<KeyValuePair<string, object?>>> DefaultPool() =>
        new DefaultObjectPool<List<KeyValuePair<string, object?>>>(new DefaultPooledObjectPolicy<List<KeyValuePair<string, object?>>>());

    private static RawLogEvent SimpleEvent(
        string message      = "test",
        LogLevel level      = LogLevel.Information,
        string category     = "TestCategory") =>
        new(level, category, message, null, null, null);

    // ── Build + delivery ──────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_BuildsEntry_WithMetadataFields()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(channel.Reader, [transport], NoOpMasking());

        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Warning, "MyService", "Something happened", null, null, null));
        channel.Writer.Complete();

        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        Dictionary<string, object?> entry = transport.Entries.Single();
        entry["level"].Should().Be("warning");
        entry["category"].Should().Be("MyService");
        entry["message"].Should().Be("Something happened");
    }

    [Fact]
    public async Task Worker_DeliversPendingEntries_ToAllTransports()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport transport1 = new();
        CapturingTransport transport2 = new();
        Sl4nTransportWorker worker = new(channel.Reader, [transport1, transport2], NoOpMasking());

        channel.Writer.TryWrite(SimpleEvent("first"));
        channel.Writer.TryWrite(SimpleEvent("second"));
        channel.Writer.Complete();

        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        transport1.Entries.Should().HaveCount(2);
        transport2.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Worker_AppliesMasking_OnStructuredState()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(channel.Reader, [transport], DefaultMasking());

        KeyValuePair<string, object?>[] state =
        [
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {Email}")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "test", "Charged john@example.com", state, null, null));
        channel.Writer.Complete();

        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        Dictionary<string, object?> entry = transport.Entries.Single();
        entry["Email"].Should().Be("j**n@example.com");
        entry.Should().NotContainKey("{OriginalFormat}");
    }

    [Fact]
    public async Task Worker_IncludesScopeFields_Unmasked()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(channel.Reader, [transport], DefaultMasking());

        List<KeyValuePair<string, object?>> scope =
        [
            KeyValuePair.Create<string, object?>("correlationId", "req-001")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "test", "ok", null, null, scope));
        channel.Writer.Complete();

        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        transport.Entries.Single()["correlationId"].Should().Be("req-001");
    }

    [Fact]
    public async Task Worker_IncludesException_WhenPresent()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(channel.Reader, [transport], NoOpMasking());

        Exception ex = new InvalidOperationException("boom");
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Error, "test", "failed", null, ex, null));
        channel.Writer.Complete();

        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        transport.Entries.Single().Should().ContainKey("exception");
    }

    [Fact]
    public async Task Worker_EmptyChannel_DeliversNothing()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(channel.Reader, [transport], NoOpMasking());

        channel.Writer.Complete();

        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        transport.Entries.Should().BeEmpty();
    }

    // ── Graceful shutdown ─────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_StopAsync_DoesNotThrow()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        Sl4nTransportWorker worker = new(channel.Reader, [new CapturingTransport()], NoOpMasking());

        await worker.StartAsync(CancellationToken.None);

        Func<Task> stop = () => worker.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Worker_DisposeAsync_DoesNotThrow()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        Sl4nTransportWorker worker = new(channel.Reader, [new CapturingTransport()], NoOpMasking());

        await worker.StartAsync(CancellationToken.None);

        Func<Task> dispose = async () => await worker.DisposeAsync();
        await dispose.Should().NotThrowAsync();
    }

    // ── Logging matrix (context-field filtering) ──────────────────────────────

    private static async Task<Dictionary<string, object?>> RunSingle(
        RawLogEvent evt, LoggingMatrix matrix)
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(channel.Reader, [transport], NoOpMasking(), matrix);

        channel.Writer.TryWrite(evt);
        channel.Writer.Complete();

        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        return transport.Entries.Single();
    }

    private static List<KeyValuePair<string, object?>> Scope(params (string, object?)[] fields) =>
        fields.Select(f => KeyValuePair.Create(f.Item1, f.Item2)).ToList();

    [Fact]
    public async Task Worker_Matrix_FiltersScopeFields_ByLevel()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["information"] = ["correlationId"],
        });
        RawLogEvent evt = new(LogLevel.Information, "test", "ok", null, null,
            Scope(("correlationId", "req-001"), ("userId", "usr-42")));

        Dictionary<string, object?> entry = await RunSingle(evt, matrix);

        entry["correlationId"].Should().Be("req-001");
        entry.Should().NotContainKey("userId"); // not whitelisted at information
    }

    [Fact]
    public async Task Worker_Matrix_Wildcard_KeepsAllScopeFields()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["default"] = ["correlationId"],
            ["error"]   = ["*"],
        });
        RawLogEvent evt = new(LogLevel.Error, "test", "boom", null, null,
            Scope(("correlationId", "req-001"), ("userId", "usr-42")));

        Dictionary<string, object?> entry = await RunSingle(evt, matrix);

        entry["correlationId"].Should().Be("req-001");
        entry["userId"].Should().Be("usr-42");
    }

    [Fact]
    public async Task Worker_Matrix_Default_AppliesToUnlistedLevel()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["default"] = ["correlationId"],
            ["error"]   = ["*"],
        });
        RawLogEvent evt = new(LogLevel.Warning, "test", "hmm", null, null,
            Scope(("correlationId", "req-001"), ("userId", "usr-42")));

        Dictionary<string, object?> entry = await RunSingle(evt, matrix);

        entry["correlationId"].Should().Be("req-001");
        entry.Should().NotContainKey("userId"); // warning falls back to default
    }

    [Fact]
    public async Task Worker_Matrix_DoesNotFilter_StructuredState()
    {
        // information only whitelists correlationId — but per-call structured state is never filtered.
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["information"] = ["correlationId"],
        });
        KeyValuePair<string, object?>[] state =
        [
            KeyValuePair.Create<string, object?>("amount", 299.90m),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {amount}"),
        ];
        RawLogEvent evt = new(LogLevel.Information, "test", "Charged 299.90", state, null,
            Scope(("correlationId", "req-001"), ("userId", "usr-42")));

        Dictionary<string, object?> entry = await RunSingle(evt, matrix);

        entry["correlationId"].Should().Be("req-001"); // context: whitelisted
        entry.Should().NotContainKey("userId");        // context: filtered
        entry["amount"].Should().Be(299.90m);          // per-call state: always emitted
    }
}
