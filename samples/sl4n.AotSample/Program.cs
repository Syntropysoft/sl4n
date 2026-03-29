using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sl4n;

// ── Bootstrap ─────────────────────────────────────────────────────────────────
ServiceCollection services = new ServiceCollection();

services.AddSl4n(cfg =>              // Action<T> overload — AOT-safe
{
    cfg.Masking.EnableDefaultRules = true;
    cfg.Context.Source             = "app";
    cfg.Context.Inbound["app"]     = new Dictionary<string, string>
    {
        ["correlationId"] = "X-Correlation-ID"
    };
});

ServiceProvider provider = services.BuildServiceProvider();

// Start the transport worker (normally managed by IHostedService / IHost)
Sl4nTransportWorker worker = provider.GetRequiredService<Sl4nTransportWorker>();
await worker.StartAsync(CancellationToken.None);

// ── Log pipeline ──────────────────────────────────────────────────────────────
ILogger logger = provider.GetRequiredService<ILoggerFactory>()
    .CreateLogger("AOT.Sample");

// Simulate a request scope (propagation pipeline)
using (Sl4nScope scope = Sl4nContext.Push(("correlationId", "aot-req-001")))
{
    logger.LogInformation("User logged in {Email}", "alice@example.com");
    logger.LogInformation("Payment {Amount} by {Email}", 299.9m, "alice@example.com");
    logger.LogWarning("Suspicious field {Password}", "p@ssw0rd!");
}

// Outside scope — context cleared
logger.LogInformation("Context cleared — correlationId: {Value}",
    Sl4nContext.Current.GetValueOrDefault("correlationId") ?? "(empty)");

// ── Graceful shutdown ─────────────────────────────────────────────────────────
await Task.Delay(200);   // let the channel drain
await worker.StopAsync(CancellationToken.None);
await provider.DisposeAsync();
