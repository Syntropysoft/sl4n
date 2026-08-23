using System.Collections;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Sl4n.Tests;

/// <summary>
/// One sink — the audit ledger — must receive the truth; every other sink keeps getting the
/// masked entry. These tests also pin what the exemption does NOT skip: the matrix and the
/// sanitizer still apply to the exempt output.
/// </summary>
public sealed class MaskingExemptionTests
{
    private sealed class CapturingTransport : ITransport
    {
        public List<Dictionary<string, object?>> Entries { get; } = new();
        // Copy the dict — the worker reuses its instances across entries
        public void Log(IReadOnlyDictionary<string, object?> entry) =>
            Entries.Add(new Dictionary<string, object?>(entry));
    }

    /// <summary>
    /// MEL hands the logger a lazy state that can hang off a request scope, so the worker gets
    /// exactly one pass at it. This counts the passes so a second projection cannot sneak in a
    /// re-enumeration.
    /// </summary>
    private sealed class CountingState : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _items;
        public int EnumerationCount { get; private set; }

        public CountingState(params KeyValuePair<string, object?>[] items) => _items = items;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            EnumerationCount++;
            return ((IEnumerable<KeyValuePair<string, object?>>)_items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static Channel<RawLogEvent> UnboundedChannel() =>
        Channel.CreateUnbounded<RawLogEvent>();

    private static MaskingEngine DefaultMasking() =>
        MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

    private static async Task DrainAsync(Sl4nTransportWorker worker, Channel<RawLogEvent> channel)
    {
        channel.Writer.Complete();
        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);
    }

    // ── Baseline: what the worker emits today, with no exempt sink in play ───────────

    [Fact]
    public async Task NoExemptSink_OutputIsByteIdentical()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport console = new();
        Sl4nTransportWorker worker = new(channel.Reader, [console], DefaultMasking());

        KeyValuePair<string, object?>[] state =
        [
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("Amount", (object?)299.9),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {Amount} for {Email}")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "Billing", "Charged 299.9 for john@example.com", state, null, null));

        await DrainAsync(worker, channel);

        Dictionary<string, object?> entry = console.Entries.Single();
        entry["level"].Should().Be("information");
        entry["category"].Should().Be("Billing");
        entry["Email"].Should().Be("j**n@example.com");
        entry["Amount"].Should().Be(299.9);
        entry["message"].Should().Be("Charged 299.9 for j**n@example.com");
        entry.Should().NotContainKey("{OriginalFormat}");
    }

    // ── The invariant a second projection must not break ─────────────────────────────

    [Fact]
    public async Task LazyState_IsEnumeratedExactlyOnce_WithNoExemptSink()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport console = new();
        Sl4nTransportWorker worker = new(channel.Reader, [console], DefaultMasking());

        CountingState state = new(
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {Email}"));
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "Billing", "Charged john@example.com", state, null, null));

        await DrainAsync(worker, channel);

        state.EnumerationCount.Should().Be(1);
    }

    [Fact]
    public async Task LazyState_IsEnumeratedExactlyOnce_EvenWithSeveralSinks()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport a = new(), b = new(), c = new();
        Sl4nTransportWorker worker = new(channel.Reader, [a, b, c], DefaultMasking());

        CountingState state = new(
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {Email}"));
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "Billing", "Charged john@example.com", state, null, null));

        await DrainAsync(worker, channel);

        state.EnumerationCount.Should().Be(1);
        a.Entries.Should().HaveCount(1);
        b.Entries.Should().HaveCount(1);
        c.Entries.Should().HaveCount(1);
    }

    // ── The exempt sink gets the truth ───────────────────────────────────────────────

    [Fact]
    public async Task ExemptSink_GetsRawValues_AndRawMessage()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport console = new(), ledger = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [console], DefaultMasking(), unmasked: [ledger]);

        KeyValuePair<string, object?>[] state =
        [
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {Email}")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "Billing", "Charged john@example.com", state, null, null));

        await DrainAsync(worker, channel);

        Dictionary<string, object?> audit = ledger.Entries.Single();
        audit["Email"].Should().Be("john@example.com");
        audit["message"].Should().Be("Charged john@example.com"); // MEL's own, never re-rendered
        audit.Should().NotContainKey("{OriginalFormat}");
    }

    [Fact]
    public async Task MaskedAndExemptSinks_SeeDifferentValues_SameEntry()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport console = new(), ledger = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [console], DefaultMasking(), unmasked: [ledger]);

        KeyValuePair<string, object?>[] state =
        [
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("Amount", (object?)299.9),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {Amount} for {Email}")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "Billing", "Charged 299.9 for john@example.com", state, null,
            null, DateTimeOffset.UnixEpoch));

        await DrainAsync(worker, channel);

        Dictionary<string, object?> masked = console.Entries.Single();
        Dictionary<string, object?> audit  = ledger.Entries.Single();

        masked["Email"].Should().Be("j**n@example.com");
        audit["Email"].Should().Be("john@example.com");
        masked["message"].Should().Be("Charged 299.9 for j**n@example.com");
        audit["message"].Should().Be("Charged 299.9 for john@example.com");

        // Same entry: everything that is not the masking decision is identical.
        audit["timestamp"].Should().Be(masked["timestamp"]);
        audit["level"].Should().Be(masked["level"]);
        audit["category"].Should().Be(masked["category"]);
        audit["Amount"].Should().Be(masked["Amount"]);
    }

    [Fact]
    public async Task LazyState_IsEnumeratedExactlyOnce_WithExemptSink()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport console = new(), ledger = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [console], DefaultMasking(), unmasked: [ledger]);

        CountingState state = new(
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("{OriginalFormat}", "Charged {Email}"));
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "Billing", "Charged john@example.com", state, null, null));

        await DrainAsync(worker, channel);

        // Two projections, one pass. The state can hang off a disposed request scope, so a second
        // enumeration would drop the entry for EVERY sink, not just the exempt one.
        state.EnumerationCount.Should().Be(1);
        console.Entries.Should().HaveCount(1);
        ledger.Entries.Should().HaveCount(1);
    }

    // ── What the exemption does NOT skip ─────────────────────────────────────────────

    [Fact]
    public async Task ExemptSink_StillSanitizes()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport ledger = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [], DefaultMasking(), unmasked: [ledger]);

        KeyValuePair<string, object?>[] state =
        [
            KeyValuePair.Create<string, object?>("note", "clean\u001b[31mred\u0000end")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "t", "msg\u001b[31mX", state, null, null));

        await DrainAsync(worker, channel);

        // Sanitizing is sink integrity (log injection), not privacy — the exemption is masking only.
        Dictionary<string, object?> audit = ledger.Entries.Single();
        audit["note"].Should().Be("cleanredend");
        audit["message"].Should().Be("msgX");
    }

    [Fact]
    public async Task ExemptSink_StillRespectsTheMatrix()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport ledger = new();
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["default"] = ["correlationId"]
        });
        Sl4nTransportWorker worker = new(
            channel.Reader, [], DefaultMasking(), matrix, unmasked: [ledger]);

        List<KeyValuePair<string, object?>> scope =
        [
            KeyValuePair.Create<string, object?>("correlationId", "abc"),
            KeyValuePair.Create<string, object?>("noise", "drop-me")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "t", "msg", null, null, scope));

        await DrainAsync(worker, channel);

        // The matrix decides what is noise, not what is secret — it applies to the exempt sink too.
        Dictionary<string, object?> audit = ledger.Entries.Single();
        audit["correlationId"].Should().Be("abc");
        audit.Should().NotContainKey("noise");
    }

    [Fact]
    public async Task ExemptSink_StillGetsRetentionMetadata()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport ledger = new();
        RetentionRegistry registry = RetentionRegistry.Create(new Dictionary<string, RetentionPolicy>
        {
            ["SOX_AUDIT_TRAIL"] = new() { Days = 2555, Class = "SOX" }
        });
        Sl4nTransportWorker worker = new(
            channel.Reader, [], DefaultMasking(), matrix: null, stats: null, onLogFailure: null,
            retention: registry, unmasked: [ledger]);

        List<KeyValuePair<string, object?>> scope =
        [
            KeyValuePair.Create<string, object?>(Sl4nRetention.Field, "SOX_AUDIT_TRAIL")
        ];
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "audit", "payment approved", null, null, scope));

        await DrainAsync(worker, channel);

        Dictionary<string, object?> audit = ledger.Entries.Single();
        audit["retention"].Should().Be("SOX_AUDIT_TRAIL");
        audit["retentionClass"].Should().Be("SOX");
        audit["retentionDays"].Should().Be(2555);
        audit.Should().NotContainKey(Sl4nRetention.Field);
    }

    // ── Failure isolation across the two groups ──────────────────────────────────────

    private sealed class ThrowingTransport : ITransport
    {
        public void Log(IReadOnlyDictionary<string, object?> entry) =>
            throw new InvalidOperationException("sink is down");
    }

    [Fact]
    public async Task ExemptSinkThrows_NormalSinksStillReceive()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport console = new();
        Sl4nStats stats = new();
        List<string> failures = [];
        Sl4nTransportWorker worker = new(
            channel.Reader, [console], DefaultMasking(), matrix: null, stats: stats,
            onLogFailure: (_, name) => failures.Add(name), retention: null,
            unmasked: [new ThrowingTransport()]);

        channel.Writer.TryWrite(new RawLogEvent(LogLevel.Information, "t", "msg", null, null, null));

        await DrainAsync(worker, channel);

        console.Entries.Should().HaveCount(1);
        failures.Should().ContainSingle().Which.Should().Be(nameof(ThrowingTransport));
        stats.Snapshot().TransportFailures.Should().Be(1);
    }

    [Fact]
    public async Task NormalSinkThrows_ExemptStillReceives()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport ledger = new();
        Sl4nStats stats = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [new ThrowingTransport()], DefaultMasking(), matrix: null, stats: stats,
            onLogFailure: null, retention: null, unmasked: [ledger]);

        channel.Writer.TryWrite(new RawLogEvent(LogLevel.Information, "t", "msg", null, null, null));

        await DrainAsync(worker, channel);

        ledger.Entries.Should().HaveCount(1);
        stats.Snapshot().TransportFailures.Should().Be(1);
    }

    // ── The reused buffers ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BothProjections_AreClearedBetweenEntries()
    {
        Channel<RawLogEvent> channel = UnboundedChannel();
        CapturingTransport console = new(), ledger = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [console], DefaultMasking(), unmasked: [ledger]);

        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "t", "first", [KeyValuePair.Create<string, object?>("Email", "a@b.com")],
            null, null));
        channel.Writer.TryWrite(new RawLogEvent(
            LogLevel.Information, "t", "second", null, null, null));

        await DrainAsync(worker, channel);

        // The second entry carries no state; the first entry's field must not survive in either buffer.
        console.Entries[1].Should().NotContainKey("Email");
        ledger.Entries[1].Should().NotContainKey("Email");
        ledger.Entries[0]["Email"].Should().Be("a@b.com");
    }

    // ── The exemption is declared with the framework's own DI ────────────────────────

    [Fact]
    public async Task ExemptSink_IsDeclaredWithStandardKeyedDI_NotABespokeRegistry()
    {
        CapturingTransport console = new(), ledger = new();
        ServiceCollection services = new();
        services.AddSl4n(_ => { });
        services.UseTransport(console);
        // No sl4n helper here on purpose: plain keyed DI, the mechanism .NET already provides.
        services.AddKeyedSingleton<ITransport>(Sl4nTransportKeys.Unmasked, ledger);

        ServiceProvider sp = services.BuildServiceProvider();
        Sl4nTransportWorker worker = sp.GetRequiredService<Sl4nTransportWorker>();
        await worker.StartAsync(CancellationToken.None);

        ILogger logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Billing");
        logger.LogInformation("Charged {Email}", "john@example.com");

        sp.GetRequiredService<Channel<RawLogEvent>>().Writer.Complete();
        await sp.GetRequiredService<Channel<RawLogEvent>>().Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        console.Entries.Single()["Email"].Should().Be("j**n@example.com");
        ledger.Entries.Single()["Email"].Should().Be("john@example.com");
    }
}
