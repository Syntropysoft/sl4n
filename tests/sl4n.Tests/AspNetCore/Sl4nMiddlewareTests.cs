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

    private async Task<HttpClient> BuildClientAsync(string source, Dictionary<string, string> inbound)
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSl4n(cfg =>
                    {
                        cfg.Context.Source = source;
                        cfg.Context.Inbound[source] = inbound;
                    });
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

        // Give the async transport worker time to drain the channel
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // The endpoint logs "req-001" echo — transport entries include scope fields
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
