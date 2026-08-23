namespace Sl4n;

/// <summary>
/// A destination for log entries — console, file, HTTP collector, database, queue. This is sl4n's
/// universal adapter: the pipeline hands over a plain dictionary and the transport decides how to
/// serialize and where to put it.
/// </summary>
public interface ITransport
{
    /// <summary>
    /// Writes one entry. Called from the worker's single reader thread, so implementations do not
    /// need to be thread-safe, but they must be quick — a slow transport delays every other sink.
    ///
    /// Throwing is survivable: the failure is isolated, counted in <see cref="Sl4nStats"/> and
    /// surfaced through <c>OnLogFailure</c>; other transports still receive the entry.
    /// </summary>
    /// <param name="entry">
    /// The built entry. The worker REUSES this dictionary across entries — copy it if you need to
    /// keep it after returning.
    /// </param>
    void Log(IReadOnlyDictionary<string, object?> entry);
}
