namespace Sl4n;

/// <summary>
/// How conceptual context field names map to the names that travel on the wire. Application code
/// only ever uses the conceptual names; the translation happens at the edges.
/// </summary>
public sealed class ContextConfig
{
    /// <summary>
    /// Which entry of <see cref="Inbound"/> applies to traffic arriving at this service, e.g.
    /// <c>"frontend"</c>. Empty means inbound extraction is off.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Per-source maps of conceptual field → inbound header name, e.g.
    /// <c>Inbound["frontend"]["correlationId"] = "X-Correlation-Id"</c>. Header lookup is
    /// case-insensitive.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Inbound  { get; set; } = new();

    /// <summary>
    /// Per-target maps of conceptual field → outgoing header/attribute name, e.g.
    /// <c>Outbound["http"]["correlationId"] = "X-Correlation-Id"</c>. Read by
    /// <see cref="Sl4nContext.GetPropagationHeaders"/> and by <see cref="Sl4nDelegatingHandler"/>.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Outbound { get; set; } = new();

    /// <summary>
    /// Field names that are auto-generated (UUID) when not present in inbound headers.
    /// Example: <c>["correlationId"]</c> — if no header maps to correlationId, a new GUID is assigned.
    /// </summary>
    public HashSet<string> AutoGenerate { get; set; } = new();

    /// <summary>
    /// Outbound target name used to set HTTP response headers from context fields.
    /// Example: <c>"response"</c> with <c>Outbound["response"] = new() { ["correlationId"] = "X-Correlation-Id" }</c>.
    /// Leave empty to disable response header injection.
    /// </summary>
    public string ResponseTarget { get; set; } = string.Empty;
}
