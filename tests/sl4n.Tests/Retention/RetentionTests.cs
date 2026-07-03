using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Sl4n.Tests;

public sealed class RetentionTests
{
    private sealed class CapturingTransport : ITransport
    {
        public List<Dictionary<string, object?>> Entries { get; } = new();
        public void Log(IReadOnlyDictionary<string, object?> entry) =>
            Entries.Add(new Dictionary<string, object?>(entry));
    }

    private static MaskingEngine NoMask() => MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = false });

    private static RetentionRegistry Registry() => RetentionRegistry.Create(new Dictionary<string, RetentionPolicy>
    {
        ["SOX_AUDIT_TRAIL"] = new RetentionPolicy { Days = 2555, Class = "SOX" },
    });

    private static List<KeyValuePair<string, object?>> Scope(params (string, object?)[] fields) =>
        fields.Select(f => KeyValuePair.Create(f.Item1, f.Item2)).ToList();

    private static async Task<Dictionary<string, object?>> Run(
        RawLogEvent evt, RetentionRegistry registry, LoggingMatrix? matrix = null)
    {
        Channel<RawLogEvent> channel = Channel.CreateUnbounded<RawLogEvent>();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [transport], NoMask(), matrix, stats: null, onLogFailure: null, retention: registry);

        channel.Writer.TryWrite(evt);
        channel.Writer.Complete();
        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        return transport.Entries.Single();
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_Null_ReturnsEmpty() =>
        RetentionRegistry.Create(null).Should().BeSameAs(RetentionRegistry.Empty);

    [Fact]
    public void Create_Empty_ReturnsEmpty() =>
        RetentionRegistry.Create(new Dictionary<string, RetentionPolicy>()).Should().BeSameAs(RetentionRegistry.Empty);

    [Fact]
    public void TryResolve_Found_ReturnsPolicy()
    {
        Registry().TryResolve("SOX_AUDIT_TRAIL", out RetentionPolicy? policy).Should().BeTrue();
        policy!.Days.Should().Be(2555);
        policy.Class.Should().Be("SOX");
    }

    [Fact]
    public void TryResolve_NotFound_ReturnsFalse() =>
        Registry().TryResolve("NOPE", out _).Should().BeFalse();

    [Fact]
    public void TryResolve_IsCaseInsensitive() =>
        Registry().TryResolve("sox_audit_trail", out _).Should().BeTrue();

    // ── Worker stamping ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_StampsRetentionMetadata_FromRegisteredPolicy()
    {
        RawLogEvent evt = new(LogLevel.Information, "audit", "ok", null, null,
            Scope((Sl4nRetention.Field, "SOX_AUDIT_TRAIL")));

        Dictionary<string, object?> entry = await Run(evt, Registry());

        entry["retention"].Should().Be("SOX_AUDIT_TRAIL");
        entry["retentionClass"].Should().Be("SOX");
        entry["retentionDays"].Should().Be(2555);
    }

    [Fact]
    public async Task Worker_DoesNotEmit_RawRetentionField()
    {
        RawLogEvent evt = new(LogLevel.Information, "audit", "ok", null, null,
            Scope((Sl4nRetention.Field, "SOX_AUDIT_TRAIL")));

        Dictionary<string, object?> entry = await Run(evt, Registry());

        entry.Should().NotContainKey(Sl4nRetention.Field);
    }

    [Fact]
    public async Task Worker_UnknownPolicy_EmitsNameOnly()
    {
        RawLogEvent evt = new(LogLevel.Information, "audit", "ok", null, null,
            Scope((Sl4nRetention.Field, "NOPE")));

        Dictionary<string, object?> entry = await Run(evt, RetentionRegistry.Empty);

        entry["retention"].Should().Be("NOPE");
        entry.Should().NotContainKey("retentionClass");
        entry.Should().NotContainKey("retentionDays");
    }

    [Fact]
    public async Task Worker_Retention_BypassesLoggingMatrix()
    {
        // matrix drops all context at information; retention must still be stamped
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]> { ["information"] = [] });
        RawLogEvent evt = new(LogLevel.Information, "audit", "ok", null, null,
            Scope((Sl4nRetention.Field, "SOX_AUDIT_TRAIL"), ("userId", "u1")));

        Dictionary<string, object?> entry = await Run(evt, Registry(), matrix);

        entry.Should().NotContainKey("userId");                  // context filtered by the matrix
        entry["retention"].Should().Be("SOX_AUDIT_TRAIL");       // retention bypassed the matrix
        entry["retentionClass"].Should().Be("SOX");
    }

    // ── End-to-end via BeginRetentionScope ──────────────────────────────────────

    [Fact]
    public async Task BeginRetentionScope_StampsMetadata_EndToEnd()
    {
        Channel<RawLogEvent> channel = Channel.CreateUnbounded<RawLogEvent>();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [transport], NoMask(), matrix: null, stats: null, onLogFailure: null, retention: Registry());

        Sl4nLoggerProvider provider = new(channel);
        provider.SetScopeProvider(new LoggerExternalScopeProvider());
        ILogger logger = provider.CreateLogger("audit");

        using (logger.BeginRetentionScope("SOX_AUDIT_TRAIL"))
            logger.LogInformation("payment approved");

        channel.Writer.Complete();
        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        Dictionary<string, object?> entry = transport.Entries.Single();
        entry["retention"].Should().Be("SOX_AUDIT_TRAIL");
        entry["retentionClass"].Should().Be("SOX");
        entry["retentionDays"].Should().Be(2555);
        entry.Should().NotContainKey(Sl4nRetention.Field);
    }
}
