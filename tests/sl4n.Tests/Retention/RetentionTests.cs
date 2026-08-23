using System.Globalization;
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

    // ── retentionUntil: the window materialised at write time ────────────────────

    private static RetentionRegistry YearsRegistry() => RetentionRegistry.Create(
        new Dictionary<string, RetentionPolicy>
        {
            ["SOX"]     = new RetentionPolicy { Years = 7,  Class = "SOX" },
            ["GDPR"]    = new RetentionPolicy { Months = 6, Class = "GDPR" },
            ["SHORT"]   = new RetentionPolicy { Days = 30,  Class = "ops" },
            ["NO_UNIT"] = new RetentionPolicy { Class = "unclassified" },
        });

    private static RawLogEvent At(DateTimeOffset when, string policy) =>
        new(LogLevel.Information, "audit", "payment approved", null, null,
            Scope((Sl4nRetention.Field, (object?)policy)), when);

    [Fact]
    public async Task Worker_StampsRetentionUntil_FromTheEventTimestamp()
    {
        Dictionary<string, object?> entry = await Run(
            At(new DateTimeOffset(2026, 8, 23, 14, 30, 0, TimeSpan.Zero), "SOX"), YearsRegistry());

        entry["retentionUntil"].Should().Be("2033-08-23");
        entry["retentionClass"].Should().Be("SOX");
    }

    [Fact]
    public async Task Worker_StampsRetentionUntil_RollingForwardOnAShortMonth()
    {
        // 31-Aug + 6 months: .NET would clamp to 28-Feb. Short ends the window early, so it rolls.
        Dictionary<string, object?> entry = await Run(
            At(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero), "GDPR"), YearsRegistry());

        entry["retentionUntil"].Should().Be("2027-03-03");
    }

    [Fact]
    public async Task Worker_AnchorsTheWindowToTheLogInstant_NotToNow()
    {
        // The worker can be far behind under backlog. A window anchored to processing time would
        // drift with the queue depth; this one is reproducible from the entry itself.
        Dictionary<string, object?> entry = await Run(
            At(new DateTimeOffset(2020, 1, 15, 0, 0, 0, TimeSpan.Zero), "SHORT"), YearsRegistry());

        entry["retentionUntil"].Should().Be("2020-02-14");
    }

    [Fact]
    public async Task Worker_UsesUtc_ForTheAnchorDate()
    {
        // 23:30 in UTC+3 is already the next day in UTC — the window must not shift with the
        // producer's offset.
        Dictionary<string, object?> entry = await Run(
            At(new DateTimeOffset(2026, 8, 23, 23, 30, 0, TimeSpan.FromHours(3)), "SHORT"),
            YearsRegistry());

        entry["retentionUntil"].Should().Be("2026-09-22"); // anchored on 2026-08-23 UTC
    }

    [Fact]
    public async Task Worker_NoUnitDeclared_EmitsNoUntil()
    {
        Dictionary<string, object?> entry = await Run(
            At(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), "NO_UNIT"), YearsRegistry());

        entry["retention"].Should().Be("NO_UNIT");
        entry["retentionClass"].Should().Be("unclassified");
        entry.Should().NotContainKey("retentionUntil"); // no window ⇒ no date invented
    }

    [Fact]
    public async Task Worker_NoTimestamp_EmitsNoUntil()
    {
        // No anchor, no date. The alternative would be reaching for a clock and stamping a window
        // the entry cannot justify.
        Dictionary<string, object?> entry = await Run(
            new RawLogEvent(LogLevel.Information, "audit", "no timestamp", null, null,
                Scope((Sl4nRetention.Field, (object?)"SOX"))),
            YearsRegistry());

        entry["retentionClass"].Should().Be("SOX");
        entry.Should().NotContainKey("retentionUntil");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-AR")]
    [InlineData("de-DE")]
    public async Task Worker_RetentionUntil_IsIso_RegardlessOfCulture(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Dictionary<string, object?> entry = await Run(
                At(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), "SOX"), YearsRegistry());

            entry["retentionUntil"].Should().Be("2033-08-23");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ── An ambiguous window stops the host, it does not ship ─────────────────────

    [Fact]
    public void Registry_PolicyWithTwoUnits_ThrowsAtStartup()
    {
        Action build = () => RetentionRegistry.Create(new Dictionary<string, RetentionPolicy>
        {
            ["CONFUSED"] = new RetentionPolicy { Days = 30, Years = 7, Class = "SOX" },
        });

        build.Should().Throw<Sl4nConfigurationException>()
             .WithMessage("*CONFUSED*Days=30*Years=7*");
    }

    [Fact]
    public void Registry_PolicyWithOneUnit_IsAccepted()
    {
        foreach (RetentionPolicy p in new[]
        {
            new RetentionPolicy { Days = 30 },
            new RetentionPolicy { Months = 6 },
            new RetentionPolicy { Years = 7 },
            new RetentionPolicy { Class = "none" },
        })
        {
            Action build = () => RetentionRegistry.Create(
                new Dictionary<string, RetentionPolicy> { ["P"] = p });
            build.Should().NotThrow();
        }
    }
}
