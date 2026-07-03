using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sl4n.Testing;
using Xunit;

namespace Sl4n.Tests;

public sealed class SpyTransportTests
{
    // ── Capture + query surface ─────────────────────────────────────────────────

    [Fact]
    public void Captures_And_CopiesEntries()
    {
        SpyTransport spy = new();
        Dictionary<string, object?> dict = new() { ["level"] = "information", ["message"] = "hi" };

        spy.Log(dict);
        dict["message"] = "mutated"; // spy must have taken a defensive copy

        spy.Count.Should().Be(1);
        spy.Entries[0]["message"].Should().Be("hi");
    }

    [Fact]
    public void AtLevel_WithField_AnyMessageContains_Filter()
    {
        SpyTransport spy = new();
        spy.Log(new Dictionary<string, object?> { ["level"] = "error", ["message"] = "boom", ["correlationId"] = "req-1" });
        spy.Log(new Dictionary<string, object?> { ["level"] = "information", ["message"] = "ok" });

        spy.AtLevel("error").Should().HaveCount(1);
        spy.WithField("correlationId", "req-1").Should().HaveCount(1);
        spy.AnyMessageContains("error", "boom").Should().BeTrue();
        spy.AnyMessageContains("information", "boom").Should().BeFalse();
    }

    [Fact]
    public void Clear_Empties()
    {
        SpyTransport spy = new();
        spy.Log(new Dictionary<string, object?> { ["level"] = "information" });

        spy.Clear();

        spy.Count.Should().Be(0);
    }

    // ── Integration through a real logger pipeline (captures masked output) ──────

    [Fact]
    public async Task Captures_MaskedOutput_ThroughPipeline()
    {
        Channel<RawLogEvent> channel = Channel.CreateUnbounded<RawLogEvent>();
        SpyTransport spy = new();
        Sl4nTransportWorker worker = new(channel.Reader, [spy],
            MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true }));
        Sl4nLoggerProvider provider = new(channel);
        ILogger logger = provider.CreateLogger("test");

        logger.LogInformation("Charged {Email}", "john@example.com");

        channel.Writer.Complete();
        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        spy.Count.Should().Be(1);
        spy.Entries[0]["Email"].Should().Be("j**n@example.com");
    }

    // ── DI wiring ────────────────────────────────────────────────────────────────

    [Fact]
    public void UseSpyTransport_MakesSpyTheSoleTransport()
    {
        SpyTransport spy = new();
        ServiceCollection services = new();
        services.AddSl4n(cfg => { });
        services.UseSpyTransport(spy);

        using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetServices<ITransport>().Should().ContainSingle().Which.Should().BeSameAs(spy);
    }
}
