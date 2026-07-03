namespace Sl4n;

/// <summary>
/// A named retention policy — compliance metadata attached to a log entry so a downstream store can
/// apply the right retention. Declared in <see cref="Sl4nConfig.RetentionPolicies"/> and tagged onto
/// logs with <see cref="Sl4nRetention.BeginRetentionScope"/>. Bindable from configuration.
/// </summary>
public sealed class RetentionPolicy
{
    /// <summary>Retention period in days (0 = unspecified).</summary>
    public int Days { get; set; }

    /// <summary>Free-form compliance class/label, e.g. <c>SOX</c>, <c>GDPR</c>, <c>audit</c>.</summary>
    public string Class { get; set; } = string.Empty;
}
