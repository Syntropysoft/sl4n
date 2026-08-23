using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;

namespace Sl4n.Benchmarks;

/// <summary>
/// Times the pipeline itself: Sl4nTransportWorker.Build() over one event, with no channel and no
/// transports.
///
/// The other benchmarks in this project time ILogger.Log, which snapshots the scope and writes to
/// a channel — the worker drains on another thread, so masking, matrix filtering, sanitization,
/// the message re-render and the dual projection never entered any number. Worse, they read
/// backwards: a slower worker fills the channel sooner, DropOldest starts discarding, and the
/// logger call gets faster. This file is the fix for that blind spot.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class WorkerBuildBenchmark
{
    private sealed class NullSink : ITransport
    {
        public void Log(IReadOnlyDictionary<string, object?> entry) { }
    }

    private Sl4nTransportWorker _plain      = null!;   // masking on, one sink
    private Sl4nTransportWorker _noRules    = null!;   // masking off
    private Sl4nTransportWorker _withMatrix = null!;
    private Sl4nTransportWorker _dual       = null!;   // masking on + one exempt sink

    private RawLogEvent _bare;
    private RawLogEvent _state;
    private RawLogEvent _scoped;
    private RawLogEvent _retained;

    private static Sl4nTransportWorker Worker(
        bool masking = true, LoggingMatrix? matrix = null, bool exempt = false,
        RetentionRegistry? retention = null) =>
        new(Channel.CreateUnbounded<RawLogEvent>().Reader,
            [new NullSink()],
            MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = masking }),
            matrix,
            stats: null,
            onLogFailure: null,
            retention: retention,
            unmasked: exempt ? [new NullSink()] : null);

    [GlobalSetup]
    public void Setup()
    {
        _plain   = Worker();
        _noRules = Worker(masking: false);
        _dual    = Worker(exempt: true);
        _withMatrix = Worker(matrix: LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["default"]     = ["correlationId"],
            ["information"] = ["correlationId", "tenantId"],
        }));

        _bare = new RawLogEvent(
            LogLevel.Information, "Billing", "nothing to see here", null, null, null,
            DateTimeOffset.UnixEpoch);

        // The README quick start: two maskable keys, so the message is re-rendered (7.5).
        KeyValuePair<string, object?>[] state =
        [
            KeyValuePair.Create<string, object?>("Amount", (object?)299.9),
            KeyValuePair.Create<string, object?>("Email", "john@example.com"),
            KeyValuePair.Create<string, object?>("Password", "hunter2"),
            KeyValuePair.Create<string, object?>("{OriginalFormat}",
                "Card charged {Amount} for {Email} pw {Password}"),
        ];
        _state = new RawLogEvent(
            LogLevel.Information, "Billing", "Card charged 299.9 for john@example.com pw hunter2",
            state, null, null, DateTimeOffset.UnixEpoch);

        List<KeyValuePair<string, object?>> scope =
        [
            KeyValuePair.Create<string, object?>("correlationId", "3f2a9c11-0d44-4e2b-9f77-1b2c3d4e5f60"),
            KeyValuePair.Create<string, object?>("tenantId", "acme"),
            KeyValuePair.Create<string, object?>("noise", "filtered-out-by-the-matrix"),
        ];
        _scoped = new RawLogEvent(
            LogLevel.Information, "Billing", "with context", state, null, scope, DateTimeOffset.UnixEpoch);

        List<KeyValuePair<string, object?>> retentionScope =
        [
            KeyValuePair.Create<string, object?>(Sl4nRetention.Field, "SOX_AUDIT_TRAIL"),
        ];
        _retained = new RawLogEvent(
            LogLevel.Information, "audit", "payment approved", state, null, retentionScope,
            DateTimeOffset.UnixEpoch);
    }

    // ── Baselines ────────────────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "bare entry (no state, no scope)")]
    public object Bare() => _plain.BuildOnly(in _bare);

    [Benchmark(Description = "3 fields, masking OFF")]
    public object NoRules() => _noRules.BuildOnly(in _state);

    // ── The pipeline as it actually runs ─────────────────────────────────────────────

    [Benchmark(Description = "3 fields, masking ON (+ message re-render)")]
    public object Masked() => _plain.BuildOnly(in _state);

    [Benchmark(Description = "3 fields + scope filtered by matrix")]
    public object Matrix() => _withMatrix.BuildOnly(in _scoped);

    // ── What the exemption costs ─────────────────────────────────────────────────────
    // Against "masking ON": the delta IS the price of the second projection. Same event,
    // same rules; the only difference is that one exempt sink is registered.

    [Benchmark(Description = "3 fields, masking ON + exempt sink (dual projection)")]
    public object DualProjection() => _dual.BuildOnly(in _state);

    // ── Retention tagging ────────────────────────────────────────────────────────────

    [Benchmark(Description = "3 fields + retention scope (unresolved policy)")]
    public object Retention() => _plain.BuildOnly(in _retained);
}
