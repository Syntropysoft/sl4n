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

    /// <summary>Builds a registry (case-insensitive names). Null/empty yields <see cref="Empty"/>.</summary>
    public static RetentionRegistry Create(IReadOnlyDictionary<string, RetentionPolicy>? policies)
    {
        if (policies is null || policies.Count == 0) return Empty;
        return new RetentionRegistry(
            new Dictionary<string, RetentionPolicy>(policies, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Resolves <paramref name="name"/> to its policy, if declared.</summary>
    public bool TryResolve(string name, [MaybeNullWhen(false)] out RetentionPolicy policy) =>
        _policies.TryGetValue(name, out policy);
}
