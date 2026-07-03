using Microsoft.Extensions.Logging;

namespace Sl4n;

/// <summary>
/// Tags logs with a retention policy — the .NET-idiomatic analog of SyntropyLog's
/// <c>logger.withRetention('SOX_AUDIT_TRAIL')</c>. It opens a MEL scope carrying the policy name;
/// every log inside the scope is stamped with the resolved policy metadata
/// (<c>retention</c>, <c>retentionClass</c>, <c>retentionDays</c>), bypassing the logging matrix.
/// </summary>
/// <example>
/// <code>
/// using (logger.BeginRetentionScope("SOX_AUDIT_TRAIL"))
/// {
///     logger.LogInformation("Payment approved {Amount}", amount);
/// }
/// </code>
/// </example>
public static class Sl4nRetention
{
    /// <summary>Internal scope key carrying the retention policy name. Consumed by the worker, never emitted raw.</summary>
    public const string Field = "__retention";

    /// <summary>Opens a scope that tags every log inside it with the named retention policy.</summary>
    public static IDisposable BeginRetentionScope(this ILogger logger, string policyName) =>
        logger.BeginScope(new Dictionary<string, object?> { [Field] = policyName }) ?? NoopScope.Instance;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
