using Microsoft.Extensions.Logging;

namespace Sl4n;

/// <summary>
/// Typed builder for a <see cref="LoggingMatrix"/> configuration. Keying levels off
/// <see cref="LogLevel"/> rather than raw strings catches "not-a-level" typos at compile time,
/// and it is AOT-safe (no configuration binding). The produced dictionary is assigned to
/// <see cref="Sl4nConfig.LoggingMatrix"/>.
/// </summary>
/// <example>
/// <code>
/// cfg.LoggingMatrix = new MatrixBuilder()
///     .Default("correlationId")
///     .Level(LogLevel.Information, "correlationId", "userId", "operation")
///     .All(LogLevel.Error)
///     .Build();
/// </code>
/// </example>
public sealed class MatrixBuilder
{
    private readonly Dictionary<string, string[]> _matrix = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets the <c>default</c> whitelist applied to any level not listed explicitly.</summary>
    public MatrixBuilder Default(params string[] fields)
    {
        _matrix[LoggingMatrix.DefaultKey] = fields;
        return this;
    }

    /// <summary>Sets the whitelist of context fields allowed at <paramref name="level"/>.</summary>
    public MatrixBuilder Level(LogLevel level, params string[] fields)
    {
        _matrix[level.ToString()] = fields;
        return this;
    }

    /// <summary>Allows every context field at <paramref name="level"/> (equivalent to <c>["*"]</c>).</summary>
    public MatrixBuilder All(LogLevel level)
    {
        _matrix[level.ToString()] = [LoggingMatrix.Wildcard];
        return this;
    }

    /// <summary>Returns the built matrix, ready to assign to <see cref="Sl4nConfig.LoggingMatrix"/>.</summary>
    public Dictionary<string, string[]> Build() => _matrix;
}
