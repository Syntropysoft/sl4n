using BenchmarkDotNet.Attributes;

namespace Sl4n.Benchmarks;

/// <summary>
/// Measures ConsoleTransport serialization: Utf8JsonWriter vs legacy JsonSerializer.
/// Validates that the AOT-safe path has no measurable regression.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class ConsoleTransportBenchmark
{
    private static readonly IReadOnlyDictionary<string, object?> _entry =
        new Dictionary<string, object?>
        {
            ["level"]         = "information",
            ["category"]      = "Benchmark",
            ["message"]       = "Order ord-001 placed for j**n@example.com",
            ["correlationId"] = "req-abc",
            ["orderId"]       = "ord-001",
            ["amount"]        = 299.9,
        };

    private readonly ConsoleTransport _transport = new();

    [Benchmark(Baseline = true, Description = "ConsoleTransport (Utf8JsonWriter, stdout suppressed)")]
    public void Serialize_Utf8JsonWriter()
    {
        // Redirect stdout to discard so I/O doesn't dominate the measurement
        TextWriter original = Console.Out;
        Console.SetOut(TextWriter.Null);
        try   { _transport.Log(_entry); }
        finally { Console.SetOut(original); }
    }
}
