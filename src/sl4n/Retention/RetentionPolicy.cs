namespace Sl4n;

/// <summary>
/// A named retention policy — compliance metadata attached to a log entry so a downstream store can
/// apply the right retention. Declared in <see cref="Sl4nConfig.RetentionPolicies"/> and tagged onto
/// logs with <see cref="Sl4nRetention.BeginRetentionScope"/>. Bindable from configuration.
/// </summary>
public sealed class RetentionPolicy
{
    /// <summary>Retention period in days (0 = unspecified).</summary>
    /// <remarks>
    /// Exact, but a calendar window expressed in days drifts: 2555 is 7 × 365 and ignores leap
    /// days, so it ends two days before seven actual years. Declare <see cref="Years"/> or
    /// <see cref="Months"/> when the window is a calendar period.
    /// </remarks>
    public int Days { get; set; }

    /// <summary>Retention period in calendar months (0 = unspecified).</summary>
    public int Months { get; set; }

    /// <summary>Retention period in calendar years (0 = unspecified).</summary>
    public int Years { get; set; }

    /// <summary>Free-form compliance class/label, e.g. <c>SOX</c>, <c>GDPR</c>, <c>audit</c>.</summary>
    public string Class { get; set; } = string.Empty;
}
