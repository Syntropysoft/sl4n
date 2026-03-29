using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Sl4n.Tests;

public sealed class Sl4nLoggerTests
{
    private sealed class CapturingTransport : ITransport
    {
        public List<Dictionary<string, object?>> Entries { get; } = new();
        // Copy the dict — worker reuses the same instance across entries
        public void Log(IReadOnlyDictionary<string, object?> entry) =>
            Entries.Add(new Dictionary<string, object?>(entry));
    }

    // Wires logger → channel → worker → transport for test assertions
    private sealed class TestLogPipeline : IAsyncDisposable
    {
        private readonly Channel<RawLogEvent>  _channel = Channel.CreateUnbounded<RawLogEvent>();
        private readonly Sl4nTransportWorker   _worker;
        public  CapturingTransport             Transport { get; } = new();
        public  Sl4nLoggerProvider             Provider  { get; }

        public TestLogPipeline(bool enableDefaultRules = true)
        {
            MaskingEngine masking = MaskingEngine.Create(
                new MaskingConfig { EnableDefaultRules = enableDefaultRules });
            Provider = new Sl4nLoggerProvider(_channel);
            _worker  = new Sl4nTransportWorker(_channel.Reader, [Transport], masking);
        }

        // Completes the writer, starts the worker, and waits until all entries are consumed.
        public async Task DrainAsync()
        {
            _channel.Writer.TryComplete();
            await _worker.StartAsync(CancellationToken.None);
            await _channel.Reader.Completion;
            await _worker.StopAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync() => new ValueTask(DrainAsync());
    }

    // ── Masking ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Log_MasksSensitiveStructuredFields()
    {
        await using TestLogPipeline pipeline = new();
        ILogger logger = pipeline.Provider.CreateLogger("test");

        logger.LogInformation("Charged {Amount} {Email}", 299.90m, "john@example.com");
        await pipeline.DrainAsync();

        IReadOnlyDictionary<string, object?> entry = pipeline.Transport.Entries.Single();
        entry["Amount"].Should().Be(299.90m);
        entry["Email"].Should().Be("j**n@example.com");
    }

    [Fact]
    public async Task Log_DoesNotMaskUnknownFields()
    {
        await using TestLogPipeline pipeline = new();
        ILogger logger = pipeline.Provider.CreateLogger("test");

        logger.LogInformation("Order {OrderId} placed", "ORD-001");
        await pipeline.DrainAsync();

        pipeline.Transport.Entries.Single()["OrderId"].Should().Be("ORD-001");
    }

    [Fact]
    public async Task Log_WithNoRules_DoesNotMaskAnything()
    {
        await using TestLogPipeline pipeline = new(enableDefaultRules: false);
        ILogger logger = pipeline.Provider.CreateLogger("test");

        logger.LogInformation("User {Email}", "john@example.com");
        await pipeline.DrainAsync();

        pipeline.Transport.Entries.Single()["Email"].Should().Be("john@example.com");
    }

    // ── Log entry metadata ────────────────────────────────────────────────────

    [Fact]
    public async Task Log_IncludesLevelCategoryAndMessage()
    {
        await using TestLogPipeline pipeline = new();
        ILogger logger = pipeline.Provider.CreateLogger("MyService");

        logger.LogWarning("Something happened");
        await pipeline.DrainAsync();

        IReadOnlyDictionary<string, object?> entry = pipeline.Transport.Entries.Single();
        entry["level"].Should().Be("warning");
        entry["category"].Should().Be("MyService");
        entry["message"].Should().Be("Something happened");
    }

    [Fact]
    public async Task Log_WithException_IncludesExceptionField()
    {
        await using TestLogPipeline pipeline = new();
        ILogger logger = pipeline.Provider.CreateLogger("test");

        logger.LogError(new InvalidOperationException("boom"), "Request failed");
        await pipeline.DrainAsync();

        pipeline.Transport.Entries.Single().Should().ContainKey("exception");
    }

    [Fact]
    public async Task Log_None_IsNotEmitted()
    {
        await using TestLogPipeline pipeline = new();
        ILogger logger = pipeline.Provider.CreateLogger("test");

        logger.Log(LogLevel.None, "Should not appear");
        await pipeline.DrainAsync();

        pipeline.Transport.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Log_AfterChannelClosed_IsNotEmitted()
    {
        await using TestLogPipeline pipeline = new();
        ILogger logger = pipeline.Provider.CreateLogger("test");

        // Close the channel — simulates Sl4nTransportWorker.StopAsync
        await pipeline.DrainAsync();

        // IsEnabled must return false — MEL skips everything, zero allocation
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.LogInformation("Should be skipped entirely");

        pipeline.Transport.Entries.Should().BeEmpty();
    }

    // ── Scope enrichment ──────────────────────────────────────────────────────

    [Fact]
    public async Task Log_IncludesScopeFields_SetViaBeginScope()
    {
        await using TestLogPipeline pipeline = new();
        LoggerExternalScopeProvider scopeProvider = new();
        pipeline.Provider.SetScopeProvider(scopeProvider);
        ILogger logger = pipeline.Provider.CreateLogger("test");

        using IDisposable scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = "req-001",
            ["traceId"]       = "trace-xyz"
        })!;

        logger.LogInformation("Payment processed");
        await pipeline.DrainAsync();

        IReadOnlyDictionary<string, object?> entry = pipeline.Transport.Entries.Single();
        entry["correlationId"].Should().Be("req-001");
        entry["traceId"].Should().Be("trace-xyz");
    }

    [Fact]
    public async Task Log_ScopeFields_AreNotMasked()
    {
        await using TestLogPipeline pipeline = new();
        LoggerExternalScopeProvider scopeProvider = new();
        pipeline.Provider.SetScopeProvider(scopeProvider);
        ILogger logger = pipeline.Provider.CreateLogger("test");

        using IDisposable scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = "req-001"
        })!;

        logger.LogInformation("Done");
        await pipeline.DrainAsync();

        pipeline.Transport.Entries.Single()["correlationId"].Should().Be("req-001");
    }
}
