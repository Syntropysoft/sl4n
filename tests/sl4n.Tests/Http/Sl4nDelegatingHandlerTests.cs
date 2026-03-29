using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Sl4n.Tests;

public sealed class Sl4nDelegatingHandlerTests
{
    // Captures the outgoing request without making a real HTTP call
    private sealed class CapturingHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static readonly ContextConfig _contextConfig = new()
    {
        Outbound = new()
        {
            ["http"]  = new() { ["correlationId"] = "X-Correlation-ID", ["traceId"] = "X-Trace-ID" },
            ["kafka"] = new() { ["correlationId"] = "correlationId" }
        }
    };

    private static (HttpClient Client, CapturingHandler Capturing) BuildClient(string target = "http")
    {
        Sl4nConfig config = new() { Context = _contextConfig };
        IOptions<Sl4nConfig> options = Options.Create(config);

        CapturingHandler capturing = new CapturingHandler();
        Sl4nDelegatingHandler handler = new Sl4nDelegatingHandler(options, target)
        {
            InnerHandler = capturing
        };

        return (new HttpClient(handler), capturing);
    }

    // ── Headers propagated ────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_InjectsOutboundHeaders_FromContext()
    {
        (HttpClient client, CapturingHandler capturing) = BuildClient("http");

        using Sl4nScope scope = Sl4nContext.Push(
            ("correlationId", "req-001"),
            ("traceId",       "trace-xyz"));

        await client.GetAsync("http://downstream/api");

        capturing.LastRequest!.Headers.GetValues("X-Correlation-ID").Should().Contain("req-001");
        capturing.LastRequest!.Headers.GetValues("X-Trace-ID").Should().Contain("trace-xyz");
    }

    [Fact]
    public async Task SendAsync_EmptyContext_NoHeadersAdded()
    {
        (HttpClient client, CapturingHandler capturing) = BuildClient("http");

        // No Sl4nContext.Push — context is empty
        await client.GetAsync("http://downstream/api");

        capturing.LastRequest!.Headers.Contains("X-Correlation-ID").Should().BeFalse();
        capturing.LastRequest!.Headers.Contains("X-Trace-ID").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_UnknownTarget_NoHeadersAdded()
    {
        (HttpClient client, CapturingHandler capturing) = BuildClient("unknown-target");

        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        await client.GetAsync("http://downstream/api");

        capturing.LastRequest!.Headers.Contains("X-Correlation-ID").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_FieldMissingFromContext_HeaderOmitted()
    {
        (HttpClient client, CapturingHandler capturing) = BuildClient("http");

        // Only correlationId in context — traceId absent
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        await client.GetAsync("http://downstream/api");

        capturing.LastRequest!.Headers.GetValues("X-Correlation-ID").Should().Contain("req-001");
        capturing.LastRequest!.Headers.Contains("X-Trace-ID").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_KafkaTarget_UsesKafkaWireNames()
    {
        (HttpClient client, CapturingHandler capturing) = BuildClient("kafka");

        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        await client.GetAsync("http://downstream/api");

        // Kafka maps correlationId → correlationId (same wire name)
        capturing.LastRequest!.Headers.GetValues("correlationId").Should().Contain("req-001");
        capturing.LastRequest!.Headers.Contains("X-Correlation-ID").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ContextIsolatedPerRequest()
    {
        (HttpClient client, CapturingHandler capturing) = BuildClient("http");

        // First request with context
        using (Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001")))
        {
            await client.GetAsync("http://downstream/api");
            capturing.LastRequest!.Headers.GetValues("X-Correlation-ID").Should().Contain("req-001");
        }

        // Second request — scope disposed, no headers
        await client.GetAsync("http://downstream/api");
        capturing.LastRequest!.Headers.Contains("X-Correlation-ID").Should().BeFalse();
    }
}
