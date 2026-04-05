namespace Sl4n;

public sealed class ContextConfig
{
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, Dictionary<string, string>> Inbound  { get; set; } = new();
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
