// NativeAOT smoke: the full consumer pipeline — DI (including KEYED DI), ILogger, channel
// worker, masking, message re-rendering, JSON console transport — compiled ahead-of-time and
// EXECUTED. Logs real PII through the README quick-start idiom and asserts both sides of the
// masking exemption: the console sees redactions, the keyed audit sink sees the truth.
// Exit 0 = the contract holds; anything else fails the aot-smoke CI job.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sl4n;

StringWriter captured = new();
TextWriter real = Console.Out;
Console.SetOut(captured);

AuditLedger ledger = new();

ServiceCollection services = new();
services.AddSl4n(cfg => cfg.Masking.EnableDefaultRules = true);
// Keyed DI is the mechanism; sl4n only supplies the key. This line is the reason the smoke
// exists in this shape — keyed resolution has to work with no JIT and no reflection.
services.AddKeyedSingleton<ITransport>(Sl4nTransportKeys.Unmasked, ledger);
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

// The other half: the exempt sink must have received what the console was not allowed to see.
// A masked audit trail proves nothing, so "no PII anywhere" is NOT the contract here.
if (ledger.Entries.Count != 1)
    return Fail($"keyed audit sink got {ledger.Entries.Count} entries, expected 1 — keyed DI did not resolve");
IReadOnlyDictionary<string, object?> audit = ledger.Entries[0];
if (audit.GetValueOrDefault("Email") as string != "john@example.com")
    return Fail($"audit sink did not get the raw email, got: {audit.GetValueOrDefault("Email")}");
if (audit.GetValueOrDefault("Password") as string != "hunter2")
    return Fail($"audit sink did not get the raw password, got: {audit.GetValueOrDefault("Password")}");
if (audit.GetValueOrDefault("message") as string is not string m || !m.Contains("john@example.com"))
    return Fail($"audit sink got a re-rendered message instead of MEL's own: {audit.GetValueOrDefault("message")}");

Console.WriteLine("AOT smoke OK — masked console output verified:");
Console.WriteLine(output.Trim());
Console.WriteLine("AOT smoke OK — keyed audit sink received the unmasked truth:");
Console.WriteLine($"  Email={audit["Email"]}  Password={audit["Password"]}");
return 0;

/// <summary>In-memory stand-in for an audit ledger. Deliberately NOT writing to the console:
/// the captured console output is asserted to contain no cleartext PII.</summary>
sealed class AuditLedger : ITransport
{
    public List<IReadOnlyDictionary<string, object?>> Entries { get; } = new();
    // Copy: the worker reuses its dictionaries across entries.
    public void Log(IReadOnlyDictionary<string, object?> entry) =>
        Entries.Add(new Dictionary<string, object?>(entry));
}
