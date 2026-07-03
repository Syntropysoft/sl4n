namespace Sl4n;

/// <summary>
/// Thread-safe runtime counters for the sl4n pipeline — the .NET equivalent of SyntropyLog's
/// <c>getStats()</c>. Resolve it from DI and call <see cref="Snapshot"/> to read current totals
/// (e.g. from a health endpoint).
/// </summary>
public sealed class Sl4nStats
{
    private long _logsProcessed;
    private long _transportFailures;
    private long _droppedEntries;
    private long _maskingFailures;

    internal void IncrLogsProcessed()    => Interlocked.Increment(ref _logsProcessed);
    internal void IncrTransportFailures()=> Interlocked.Increment(ref _transportFailures);
    internal void IncrDroppedEntries()   => Interlocked.Increment(ref _droppedEntries);
    internal void IncrMaskingFailures()  => Interlocked.Increment(ref _maskingFailures);

    /// <summary>Reads a consistent point-in-time snapshot of all counters.</summary>
    public Sl4nStatsSnapshot Snapshot() => new(
        Interlocked.Read(ref _logsProcessed),
        Interlocked.Read(ref _transportFailures),
        Interlocked.Read(ref _droppedEntries),
        Interlocked.Read(ref _maskingFailures));
}

/// <summary>Immutable snapshot of <see cref="Sl4nStats"/> counters.</summary>
/// <param name="LogsProcessed">Entries pulled from the channel and built.</param>
/// <param name="TransportFailures">Times a transport's <c>Log</c> threw (isolated, non-fatal).</param>
/// <param name="DroppedEntries">Entries skipped because their lazy state was already disposed.</param>
/// <param name="MaskingFailures">Times masking a field failed (custom-mask error or regex timeout).</param>
public readonly record struct Sl4nStatsSnapshot(
    long LogsProcessed,
    long TransportFailures,
    long DroppedEntries,
    long MaskingFailures);
