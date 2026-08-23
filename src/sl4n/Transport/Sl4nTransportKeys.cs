namespace Sl4n;

/// <summary>
/// DI keys sl4n recognises on an <see cref="ITransport"/> registration. Register through the
/// framework's own keyed DI — sl4n supplies the key, not the mechanism.
/// </summary>
public static class Sl4nTransportKeys
{
    /// <summary>
    /// Marks a sink that must receive values BEFORE masking — the audit ledger, where <c>2*****9</c>
    /// proves nothing. The exemption is declared by the application at registration, so a transport
    /// can never exempt itself. It skips masking and nothing else: the logging matrix still filters
    /// context fields and the sanitizer still strips control characters, exactly as for every other
    /// sink. A sink registered without this key is masked like the rest.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddKeyedSingleton&lt;ITransport&gt;(Sl4nTransportKeys.Unmasked, new AuditLedgerTransport());
    /// </code>
    /// </example>
    public const string Unmasked = "sl4n:unmasked";
}
