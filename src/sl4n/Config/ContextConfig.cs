namespace Sl4n;

public sealed class ContextConfig
{
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, Dictionary<string, string>> Inbound  { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Outbound { get; set; } = new();
}
