// NativeAOT smoke: the full consumer pipeline — DI, ILogger, channel worker, masking,
// message re-rendering, JSON console transport — compiled ahead-of-time and EXECUTED.
// Logs real PII through the README quick-start idiom and asserts the emitted JSON.
// Exit 0 = the contract holds; anything else fails the aot-smoke CI job.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sl4n;

StringWriter captured = new();
TextWriter real = Console.Out;
Console.SetOut(captured);

ServiceCollection services = new();
services.AddSl4n(cfg => cfg.Masking.EnableDefaultRules = true);
ServiceProvider provider = services.BuildServiceProvider();

IHostedService worker = provider.GetServices<IHostedService>().Single();
await worker.StartAsync(CancellationToken.None);

ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("AotSmoke");
logger.LogInformation(
    "Card charged {Amount} for {Email} pw {Password} SMOKE-MARKER",
    299.9, "john@example.com", "hunter2");

await Task.Delay(1000);                              // the worker drains the channel async
await worker.StopAsync(CancellationToken.None);
await provider.DisposeAsync();
Console.SetOut(real);

string output = captured.ToString();

int Fail(string why)
{
    Console.Error.WriteLine($"AOT SMOKE FAILED: {why}\n--- captured output ---\n{output}");
    return 1;
}

if (!output.Contains("SMOKE-MARKER"))     return Fail("log line was never emitted");
if (output.Contains("john@example.com"))  return Fail("cleartext email leaked (message or field)");
if (output.Contains("hunter2"))           return Fail("cleartext password leaked (message or field)");
if (!output.Contains("j**n@example.com")) return Fail("masked email not found");

Console.WriteLine("AOT smoke OK — masked output verified:");
Console.WriteLine(output.Trim());
return 0;
