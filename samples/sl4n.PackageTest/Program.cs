using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sl4n;

IHost host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSl4n(cfg =>
        {
            cfg.Masking.EnableDefaultRules = true;
            cfg.Context.Source             = "test";
            cfg.Context.Inbound["test"]    = new Dictionary<string, string>
            {
                ["correlationId"] = "X-Correlation-ID"
            };
        });
    })
    .StartAsync();

ILogger logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("PackageTest");

Console.WriteLine("── sl4n 1.0.0 package test ──────────────────");

// Masking
logger.LogInformation("Login {Email} pwd {Password}", "alice@example.com", "s3cr3t!");

// Context propagation
using (Sl4nScope scope = Sl4nContext.Push(("correlationId", "pkg-test-001")))
{
    logger.LogInformation("Inside scope — correlationId: {Value}",
        Sl4nContext.Current["correlationId"]);
}

// Scope cleared
logger.LogInformation("Outside scope — context empty: {Empty}",
    Sl4nContext.Current.IsEmpty);

await Task.Delay(200);
await host.StopAsync();

Console.WriteLine("── done ──────────────────────────────────────");
