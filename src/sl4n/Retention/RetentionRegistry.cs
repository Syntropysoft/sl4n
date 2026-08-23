using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Sl4n;

/// <summary>
/// Resolves retention policy names to <see cref="RetentionPolicy"/> metadata — the .NET equivalent
/// of SyntropyLog's retention registry. Built once from <see cref="Sl4nConfig.RetentionPolicies"/>.
/// </summary>
public sealed class RetentionRegistry
{
    private readonly IReadOnlyDictionary<string, RetentionPolicy> _policies;

    /// <summary>An empty registry that resolves no policy.</summary>
    public static readonly RetentionRegistry Empty =
        new(new Dictionary<string, RetentionPolicy>());

    /// <summary>
    /// Builds a registry over a COPY of <paramref name="policies"/>, keyed case-insensitively.
    /// <see cref="Create"/> is the validating entry point.
    /// </summary>
    /// <remarks>
    /// The copy is the point. A compliance registry that could change after construction would let
    /// a window be redefined for records already written under the old one — so neither the caller's
    /// dictionary nor a downcast of <see cref="Policies"/> can reach inside.
    /// </remarks>
    public RetentionRegistry(IReadOnlyDictionary<string, RetentionPolicy> policies) =>
        _policies = new ReadOnlyDictionary<string, RetentionPolicy>(
            new Dictionary<string, RetentionPolicy>(policies, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Builds a registry (case-insensitive names). Null/empty yields <see cref="Empty"/>.
    /// Throws <see cref="Sl4nConfigurationException"/> if a policy declares more than one unit —
    /// see <see cref="Validate"/>.
    /// </summary>
    public static RetentionRegistry Create(IReadOnlyDictionary<string, RetentionPolicy>? policies)
    {
        if (policies is null || policies.Count == 0) return Empty;
        Validate(policies);
        return new RetentionRegistry(policies);
    }

    /// <summary>
    /// Rejects a policy that declares Days AND Months AND/OR Years. This runs while the services
    /// are built — at startup, not on the logging path — because an ambiguous compliance window has
    /// no safe default: picking one silently would mean records swept on a date nobody chose.
    /// Failing to start is loud, immediate, and fixable; the alternative is discovered at audit time.
    /// </summary>
    private static void Validate(IReadOnlyDictionary<string, RetentionPolicy> policies)
    {
        foreach (KeyValuePair<string, RetentionPolicy> kv in policies)
        {
            if (!RetentionWindow.HasAmbiguousUnit(kv.Value)) continue;

            throw new Sl4nConfigurationException(
                $"Retention policy '{kv.Key}' declares more than one unit " +
                $"(Days={kv.Value.Days}, Months={kv.Value.Months}, Years={kv.Value.Years}). " +
                "Declare exactly one: Days, Months or Years.");
        }
    }

    /// <summary>
    /// Every registered policy, keyed case-insensitively. Read-only: handing out the live map would
    /// let a consumer redefine a compliance window at runtime, including for records already
    /// written under the old one.
    /// </summary>
    public IReadOnlyDictionary<string, RetentionPolicy> Policies => _policies;

    /// <summary>Resolves <paramref name="name"/> to its policy, if declared. Quiet on a miss.</summary>
    public bool TryResolve(string name, [MaybeNullWhen(false)] out RetentionPolicy policy) =>
        _policies.TryGetValue(name, out policy);

    /// <summary>
    /// Resolves <paramref name="name"/>, or throws <see cref="RetentionPolicyNotFoundException"/>.
    /// This is the form for a write path outside the logger: a miss there is a record persisted with
    /// no retention, so it fails loudly instead of returning null. Use <see cref="TryResolve"/> when
    /// a miss is a branch rather than a bug.
    /// </summary>
    public RetentionPolicy Resolve(string name) =>
        _policies.TryGetValue(name, out RetentionPolicy? policy)
            ? policy
            : throw new RetentionPolicyNotFoundException(name, _policies.Keys);

    /// <summary>
    /// The date <paramref name="name"/>'s window ends, counted from <paramref name="at"/> in UTC —
    /// the same answer, from the same registry and the same arithmetic, that the logger stamps as
    /// <c>retentionUntil</c>. Null when the policy declares no unit. Throws if the name is unknown.
    /// </summary>
    /// <param name="name">A registered policy name.</param>
    /// <param name="at">When the record is written. Converted to UTC, as on the logging path.</param>
    public DateOnly? Until(string name, DateTimeOffset at) =>
        RetentionWindow.Until(DateOnly.FromDateTime(at.UtcDateTime), Resolve(name));
}
