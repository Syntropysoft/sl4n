using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLog.Extensions.Logging;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Sl4n.Benchmarks;

/// <summary>
/// Realistic web-request scenario: each logger has an active scope with two propagation
/// fields (correlationId + traceId, pushed once by "middleware" in GlobalSetup) and logs
/// a structured message with a sensitive field (Email → masked by sl4n).
///
/// Conditions are identical for every logger — scope active, same message template,
/// same field values. This is what a real endpoint handler call looks like.
///
/// Run with: dotnet run -c Release -- --filter *ComparativeBenchmark*
/// </summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class ComparativeBenchmark
{
    private ILogger _melNoop    = null!;
    private ILogger _melWorking = null!;
    private ILogger _sl4n       = null!;
    private ILogger _serilog    = null!;
    private ILogger _nlog       = null!;

    private IHost                _melNoopHost    = null!;
    private IHost                _melWorkingHost = null!;
    private IHost                _sl4nHost       = null!;
    private IHost                _serilogHost    = null!;
    private IHost                _nlogHost       = null!;
    private Serilog.Core.Logger  _serilogRaw     = null!;

    // Active scopes — pushed once in GlobalSetup, kept for the entire benchmark run.
    // Simulates what Sl4nMiddleware (or any request middleware) would push per request.
    private IDisposable? _melNoopScope;
    private IDisposable? _melWorkingScope;
    private IDisposable? _sl4nScope;
    private IDisposable? _serilogScope;
    private IDisposable? _nlogScope;

    private static readonly Dictionary<string, object?> RequestScope = new()
    {
        ["correlationId"] = "req-abc-123-def-456",
        ["traceId"]       = "trace-xyz-789-uvw-012"
    };

    [GlobalSetup]
    public async Task Setup()
    {
        // ── MEL no-op (NullLogger — IsEnabled=false, does nothing) ──────────
        _melNoopHost = await new HostBuilder()
            .ConfigureServices(services =>
                services.AddLogging(b => b.ClearProviders().AddProvider(new NullLoggerProvider())))
            .StartAsync();
        _melNoop      = _melNoopHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
        _melNoopScope = _melNoop.BeginScope(RequestScope);

        // ── MEL working (scope + formats + builds dict, no masking, no channel) ─
        _melWorkingHost = await new HostBuilder()
            .ConfigureServices(services =>
                services.AddLogging(b => b.ClearProviders().AddProvider(new WorkingNullLoggerProvider())))
            .StartAsync();
        _melWorking      = _melWorkingHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
        _melWorkingScope = _melWorking.BeginScope(RequestScope);

        // ── sl4n (masking on, NullTransport) ─────────────────────────────────
        _sl4nHost = await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSl4n(cfg => cfg.Masking.EnableDefaultRules = true);
                ServiceDescriptor? console = services.FirstOrDefault(d => d.ImplementationType == typeof(ConsoleTransport));
                if (console is not null) services.Remove(console);
                services.AddSingleton<ITransport, NullTransport>();
            })
            .StartAsync();
        _sl4n      = _sl4nHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
        _sl4nScope = _sl4n.BeginScope(RequestScope);

        // ── Serilog (no sinks) ───────────────────────────────────────────────
        _serilogRaw = new Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .CreateLogger();

        _serilogHost = await new HostBuilder()
            .ConfigureServices(services =>
                services.AddLogging(b =>
                {
                    b.ClearProviders();
                    b.AddProvider(new SerilogLoggerProvider(_serilogRaw, dispose: false));
                }))
            .StartAsync();
        _serilog      = _serilogHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
        _serilogScope = _serilog.BeginScope(RequestScope);

        // ── NLog (NullTarget) ─────────────────────────────────────────────────
        NLog.Config.LoggingConfiguration nlogConfig = new NLog.Config.LoggingConfiguration();
        NLog.Targets.NullTarget nlogNull = new NLog.Targets.NullTarget();
        nlogConfig.AddRuleForAllLevels(nlogNull);
        NLog.LogManager.Configuration = nlogConfig;

        _nlogHost = await new HostBuilder()
            .ConfigureServices(services =>
                services.AddLogging(b =>
                {
                    b.ClearProviders();
                    b.AddNLog();
                }))
            .StartAsync();
        _nlog      = _nlogHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
        _nlogScope = _nlog.BeginScope(RequestScope);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _melNoopScope?.Dispose();
        _melWorkingScope?.Dispose();
        _sl4nScope?.Dispose();
        _serilogScope?.Dispose();
        _nlogScope?.Dispose();

        await _melNoopHost.StopAsync();
        await _melWorkingHost.StopAsync();
        await _sl4nHost.StopAsync();
        await _serilogHost.StopAsync();
        await _nlogHost.StopAsync();
        _serilogRaw.Dispose();
        NLog.LogManager.Shutdown();
    }

    // ── Benchmarks ────────────────────────────────────────────────────────────
    // Realistic log call: structured message with a sensitive field (Email).
    // All loggers have the same active scope (correlationId + traceId).

    [Benchmark(Baseline = true, Description = "MEL no-op (NullLogger)")]
    public void Mel_Noop()
        => _melNoop.LogInformation("Card charged {Amount} for {Email}", 299.90m, "john@example.com");

    [Benchmark(Description = "MEL working (scope + dict, no masking)")]
    public void Mel_Working()
        => _melWorking.LogInformation("Card charged {Amount} for {Email}", 299.90m, "john@example.com");

    [Benchmark(Description = "sl4n (scope + masking, null transport)")]
    public void Sl4n()
        => _sl4n.LogInformation("Card charged {Amount} for {Email}", 299.90m, "john@example.com");

    [Benchmark(Description = "Serilog (scope, no sinks, via MEL)")]
    public void Serilog()
        => _serilog.LogInformation("Card charged {Amount} for {Email}", 299.90m, "john@example.com");

    [Benchmark(Description = "NLog (scope, NullTarget, via MEL)")]
    public void NLog_Logger()
        => _nlog.LogInformation("Card charged {Amount} for {Email}", 299.90m, "john@example.com");
}
