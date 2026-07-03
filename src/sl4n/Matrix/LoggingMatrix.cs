namespace Sl4n;

/// <summary>
/// The Logging Matrix — a declarative whitelist that decides <b>which context (scope) fields appear
/// at each log level</b>. A field not whitelisted for a given level never reaches a transport.
/// Per-call structured state is not governed by the matrix (it is always emitted and masked);
/// the matrix filters only the auto-propagating context.
/// </summary>
/// <remarks>
/// Keys are MEL level names (<c>Trace</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>,
/// <c>Error</c>, <c>Critical</c>) plus <see cref="DefaultKey"/>, matched case-insensitively.
/// A level whose list contains <see cref="Wildcard"/> allows every context field.
/// </remarks>
public sealed class LoggingMatrix
{
    /// <summary>Fallback key applied to any level not listed explicitly.</summary>
    public const string DefaultKey = "default";

    /// <summary>Wildcard entry that allows every context field for a level.</summary>
    public const string Wildcard = "*";

    private static readonly HashSet<string> EmptySet = new();

    // level name (case-insensitive) → allowed field set. A set containing "*" means allow-all.
    private readonly Dictionary<string, HashSet<string>> _byLevel;
    private readonly bool _configured;

    /// <summary>An empty matrix that lets every context field through at every level.</summary>
    public static readonly LoggingMatrix Empty =
        new(new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase), configured: false);

    private LoggingMatrix(Dictionary<string, HashSet<string>> byLevel, bool configured)
    {
        _byLevel     = byLevel;
        _configured  = configured;
    }

    /// <summary>
    /// Builds a matrix from configuration. An empty or null map yields <see cref="Empty"/>
    /// (no filtering — every field passes).
    /// </summary>
    public static LoggingMatrix Create(IReadOnlyDictionary<string, string[]>? matrix)
    {
        if (matrix is null || matrix.Count == 0) return Empty;

        Dictionary<string, HashSet<string>> byLevel = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string[]> kv in matrix)
            byLevel[kv.Key] = new HashSet<string>(kv.Value ?? [], StringComparer.OrdinalIgnoreCase);

        return new LoggingMatrix(byLevel, configured: true);
    }

    /// <summary>
    /// Returns the set of context field names allowed at <paramref name="levelName"/>, or
    /// <c>null</c> when <b>every</b> field is allowed (no matrix configured, or the resolved level
    /// maps to <see cref="Wildcard"/>). A configured matrix with neither the level nor a
    /// <see cref="DefaultKey"/> entry drops all context (returns an empty set) — always define a default.
    /// </summary>
    public HashSet<string>? AllowedFields(string levelName)
    {
        if (!_configured) return null;

        if (!_byLevel.TryGetValue(levelName, out HashSet<string>? allowed))
            _byLevel.TryGetValue(DefaultKey, out allowed);

        if (allowed is null) return EmptySet;          // configured but no level & no default → drop all
        if (allowed.Contains(Wildcard)) return null;   // "*" → allow all
        return allowed;
    }
}
