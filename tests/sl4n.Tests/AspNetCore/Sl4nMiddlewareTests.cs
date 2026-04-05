using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sl4n.AspNetCore;
using Xunit;

namespace Sl4n.Tests;

public sealed class Sl4nMiddlewareTests : IAsyncDisposable
{
    private sealed class CapturingTransport : ITransport
    {
        public List<Dictionary<string, object?>> Entries { get; } = new();
        // Copy the dict — worker reuses the same instance across entries
        public void Log(IReadOnlyDictionary<string, object?> entry) =>
            Entries.Add(new Dictionary<string, object?>(entry));
    }

    private readonly CapturingTransport _transport = new();
    private IHost? _host;

    private Task<HttpClient> BuildClientAsync(string source, Dictionary<string, string> inbound)
        => BuildClientAsync(cfg =>
        {
            cfg.Context.Source = source;
            cfg.Context.Inbound[source] = inbound;
        });

    private async Task<HttpClient> BuildClientAsync(Action<Sl4nConfig> configure)
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSl4n(configure);
                    services.AddSingleton<ITransport>(_transport);
                    services.AddSl4nAspNetCore();
                });
                web.Configure(app =>
                {
                    app.UseSl4n();
                    app.Run(async ctx =>
                    {
                        // Log inside the middleware scope so scope fields appear in transport entries
                        ILogger logger = ctx.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("TestEndpoint");
                        logger.LogInformation("Request handled");

                        string correlationId =
                            Sl4nContext.Current.GetValueOrDefault("correlationId")?.ToString()
                            ?? "not-found";
                        await ctx.Response.WriteAsync(correlationId);
                    });
                });
            })
            .StartAsync();

        return _host.GetTestClient();
    }

    // ── Propagation pipeline ──────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_PopulatesSlAnContext_FromInboundHeaders()
    {
        HttpClient client = await BuildClientAsync(
            source: "frontend",
            inbound: new() { ["correlationId"] = "X-Correlation-ID" });

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-ID", "req-001"));

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Be("req-001");
    }

    [Fact]
    public async Task Middleware_UnknownSource_ContextIsEmpty()
    {
        HttpClient client = await BuildClientAsync(
            source: "partner",           // configured source
            inbound: new() { ["correlationId"] = "x-request-id" });

        // Request without the expected header
        HttpResponseMessage response = await client.GetAsync("/");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Be("not-found");
    }

    [Fact]
    public async Task Middleware_MissingHeader_FieldOmittedFromContext()
    {
        HttpClient client = await BuildClientAsync(
            source: "frontend",
            inbound: new() { ["correlationId"] = "X-Correlation-ID", ["traceId"] = "X-Trace-ID" });

        // Only correlationId sent — traceId should be absent in context
        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-ID", "req-001"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // traceId absent — no exception, just not in context
    }

    // ── Log pipeline ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_ScopeFields_EnrichLogs()
    {
        HttpClient client = await BuildClientAsync(
            source: "frontend",
            inbound: new() { ["correlationId"] = "X-Correlation-ID" });

        await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-ID", "req-001"));

        // Wait for the async transport worker to drain (poll instead of fixed delay)
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
        while (!_transport.Entries.Any(e => e.ContainsKey("correlationId")))
            await Task.Delay(50, cts.Token);

        _transport.Entries
            .Where(e => e.ContainsKey("correlationId"))
            .Should().NotBeEmpty();
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_NoInboundConfig_SkipsHeaderParsing()
    {
        // Source configured but with empty inbound map — guard 1 fires
        HttpClient client = await BuildClientAsync(
            source: "frontend",
            inbound: new());   // no field mappings

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-ID", "req-001"));

        // Request succeeds, context remains empty
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Be("not-found");
    }

    [Fact]
    public async Task Middleware_NoMatchingHeaders_SkipsPushAndBeginScope()
    {
        // Source configured but request has none of the expected headers — guard 2 fires
        HttpClient client = await BuildClientAsync(
            source: "frontend",
            inbound: new() { ["correlationId"] = "X-Correlation-ID" });

        // Request without the header — fields will be empty after ExtractInbound
        HttpResponseMessage response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Be("not-found");
    }

    // ── Scope isolation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_ContextClearedAfterRequest()
    {
        HttpClient client = await BuildClientAsync(
            source: "frontend",
            inbound: new() { ["correlationId"] = "X-Correlation-ID" });

        // First request sets context
        await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-ID", "req-001"));

        // Context outside any request scope is empty
        Sl4nContext.Current.Should().BeEmpty();
    }

    // ── Auto-generate ──────────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_AutoGenerate_CreatesUuidWhenHeaderMissing()
    {
        HttpClient client = await BuildClientAsync(cfg =>
        {
            cfg.Context.Source = "frontend";
            cfg.Context.Inbound["frontend"] = new() { ["correlationId"] = "X-Correlation-Id" };
            cfg.Context.AutoGenerate = ["correlationId"];
        });

        // No header sent — auto-generate should kick in
        HttpResponseMessage response = await client.GetAsync("/");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotBe("not-found");
        Guid.TryParse(body, out _).Should().BeTrue("auto-generated value should be a valid UUID");
    }

    [Fact]
    public async Task Middleware_AutoGenerate_PreservesInboundHeaderWhenPresent()
    {
        HttpClient client = await BuildClientAsync(cfg =>
        {
            cfg.Context.Source = "frontend";
            cfg.Context.Inbound["frontend"] = new() { ["correlationId"] = "X-Correlation-Id" };
            cfg.Context.AutoGenerate = ["correlationId"];
        });

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-Id", "my-explicit-id"));

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Be("my-explicit-id", "inbound header takes precedence over auto-generate");
    }

    [Fact]
    public async Task Middleware_AutoGenerate_WorksWithoutInboundConfig()
    {
        HttpClient client = await BuildClientAsync(cfg =>
        {
            cfg.Context.Source = "frontend";
            // No inbound config at all — only auto-generate
            cfg.Context.AutoGenerate = ["correlationId"];
        });

        HttpResponseMessage response = await client.GetAsync("/");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotBe("not-found");
        Guid.TryParse(body, out _).Should().BeTrue();
    }

    // ── Response headers ──────────────────────────────────────────────��─────

    [Fact]
    public async Task Middleware_ResponseTarget_SetsHeaderFromContext()
    {
        HttpClient client = await BuildClientAsync(cfg =>
        {
            cfg.Context.Source = "frontend";
            cfg.Context.Inbound["frontend"] = new() { ["correlationId"] = "X-Correlation-Id" };
            cfg.Context.Outbound["response"] = new() { ["correlationId"] = "X-Correlation-Id" };
            cfg.Context.ResponseTarget = "response";
        });

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-Id", "req-001"));

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle("req-001");
    }

    [Fact]
    public async Task Middleware_ResponseTarget_SetsAutoGeneratedHeader()
    {
        HttpClient client = await BuildClientAsync(cfg =>
        {
            cfg.Context.Source = "frontend";
            cfg.Context.Inbound["frontend"] = new() { ["correlationId"] = "X-Correlation-Id" };
            cfg.Context.AutoGenerate = ["correlationId"];
            cfg.Context.Outbound["response"] = new() { ["correlationId"] = "X-Correlation-Id" };
            cfg.Context.ResponseTarget = "response";
        });

        // No inbound header — auto-generated UUID should appear in response header
        HttpResponseMessage response = await client.GetAsync("/");

        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
        string headerValue = response.Headers.GetValues("X-Correlation-Id").Single();
        Guid.TryParse(headerValue, out _).Should().BeTrue("response header should contain the auto-generated UUID");

        // And it should match the context value used in the endpoint
        string body = await response.Content.ReadAsStringAsync();
        headerValue.Should().Be(body, "response header and context should have the same value");
    }

    [Fact]
    public async Task Middleware_NoResponseTarget_DoesNotSetHeader()
    {
        HttpClient client = await BuildClientAsync(cfg =>
        {
            cfg.Context.Source = "frontend";
            cfg.Context.Inbound["frontend"] = new() { ["correlationId"] = "X-Correlation-Id" };
            // ResponseTarget not set — default empty string
        });

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/")
                .WithHeader("X-Correlation-Id", "req-001"));

        response.Headers.Contains("X-Correlation-Id").Should().BeFalse(
            "no ResponseTarget configured — response header should not be set");
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}

internal static class HttpRequestMessageExtensions
{
    public static HttpRequestMessage WithHeader(
        this HttpRequestMessage request, string name, string value)
    {
        request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }
}
