using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sl4n.Benchmarks;

/// <summary>
/// Compares sl4n hot-path allocations against bare MEL.
/// Run with: dotnet run -c Release
/// </summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class Sl4nLoggerBenchmark
{
    private ILogger _sl4nLogger   = null!;
    private ILogger _melLogger    = null!;
    private IHost   _sl4nHost     = null!;
    private IHost   _melHost      = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        // sl4n host — NullTransport so no I/O noise
        _sl4nHost = await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSl4n(cfg => cfg.Masking.EnableDefaultRules = true);
                // Remove the default ConsoleTransport (writes to stdout, breaks BDN child-process comms)
                ServiceDescriptor? console = services.FirstOrDefault(d => d.ImplementationType == typeof(ConsoleTransport));
                if (console is not null) services.Remove(console);
                services.AddSingleton<ITransport, NullTransport>();
            })
            .StartAsync();

        _sl4nLogger = _sl4nHost.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Benchmark");

        // bare MEL host — no-op provider
        _melHost = await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(b => b.ClearProviders().AddProvider(new NullLoggerProvider()));
            })
            .StartAsync();

        _melLogger = _melHost.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Benchmark");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _sl4nHost.StopAsync();
        await _melHost.StopAsync();
    }

    // ── Benchmarks ────────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "MEL bare (no-op)")]
    public void Mel_LogInformation()
        => _melLogger.LogInformation("Order {OrderId} placed for {Email}", "ord-001", "john@example.com");

    [Benchmark(Description = "sl4n (masking on, null transport)")]
    public void Sl4n_LogInformation()
        => _sl4nLogger.LogInformation("Order {OrderId} placed for {Email}", "ord-001", "john@example.com");

    [Benchmark(Description = "sl4n IsEnabled=false (guard)")]
    public bool Sl4n_IsEnabled_False()
        => _sl4nLogger.IsEnabled(LogLevel.None);
}
