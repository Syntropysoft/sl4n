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

    public RetentionRegistry(IReadOnlyDictionary<string, RetentionPolicy> policies) => _policies = policies;

    /// <summary>
    /// Builds a registry (case-insensitive names). Null/empty yields <see cref="Empty"/>.
    /// Throws <see cref="Sl4nConfigurationException"/> if a policy declares more than one unit —
    /// see <see cref="Validate"/>.
    /// </summary>
    public static RetentionRegistry Create(IReadOnlyDictionary<string, RetentionPolicy>? policies)
    {
        if (policies is null || policies.Count == 0) return Empty;
        Validate(policies);
        return new RetentionRegistry(
            new Dictionary<string, RetentionPolicy>(policies, StringComparer.OrdinalIgnoreCase));
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

    /// <summary>Resolves <paramref name="name"/> to its policy, if declared.</summary>
    public bool TryResolve(string name, [MaybeNullWhen(false)] out RetentionPolicy policy) =>
        _policies.TryGetValue(name, out policy);
}
