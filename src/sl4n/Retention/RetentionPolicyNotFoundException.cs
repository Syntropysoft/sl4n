namespace Sl4n;

/// <summary>
/// Thrown by <see cref="RetentionRegistry.Resolve"/> and <see cref="RetentionRegistry.Until"/> when
/// the requested policy is not registered.
///
/// It throws rather than returning null on purpose: the caller is a write path deciding how long to
/// keep a record, and a null there persists the record with no retention at all — a gap discovered
/// at audit time, not at deploy time. Use <see cref="RetentionRegistry.TryResolve"/> when a miss is
/// a legitimate branch rather than a bug.
/// </summary>
public sealed class RetentionPolicyNotFoundException : Exception
{
    /// <summary>The name that did not resolve.</summary>
    public string PolicyName { get; }

    /// <summary>Every registered name, sorted — usually enough to spot the typo.</summary>
    public IReadOnlyList<string> AvailablePolicies { get; }

    /// <param name="policyName">The name that did not resolve.</param>
    /// <param name="available">Every registered name.</param>
    public RetentionPolicyNotFoundException(string policyName, IEnumerable<string> available)
        : base(BuildMessage(policyName, available = available.OrderBy(n => n, StringComparer.Ordinal).ToArray()))
    {
        PolicyName        = policyName;
        AvailablePolicies = (IReadOnlyList<string>)available;
    }

    private static string BuildMessage(string policyName, IEnumerable<string> sorted)
    {
        string names = string.Join(", ", sorted);
        return names.Length == 0
            ? $"Retention policy '{policyName}' is not registered, and no policies are configured."
            : $"Retention policy '{policyName}' is not registered. Available: {names}.";
    }
}
