using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Sl4n.Tests;

/// <summary>
/// MEL pre-formats the message with RAW values, so template-interpolated PII
/// ("charged {Email}") used to leak inside <c>message</c> while the Email FIELD sat
/// masked right next to it — looking masked without being masked. The worker now
/// re-renders the message from the masked values whenever a state key has a rule.
/// The README quick start output is the contract this locks in.
/// </summary>
public sealed class MessageMaskingTests
{
    private sealed class CapturingTransport : ITransport
    {
        public List<Dictionary<string, object?>> Entries { get; } = new();
        public void Log(IReadOnlyDictionary<string, object?> entry) =>
            Entries.Add(new Dictionary<string, object?>(entry));
    }

    private static async Task<Dictionary<string, object?>> RunOne(RawLogEvent e)
    {
        Channel<RawLogEvent> channel = Channel.CreateUnbounded<RawLogEvent>();
        CapturingTransport transport = new();
        Sl4nTransportWorker worker = new(
            channel.Reader, [transport],
            MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true }));

        channel.Writer.TryWrite(e);
        channel.Writer.Complete();
        await worker.StartAsync(CancellationToken.None);
        await channel.Reader.Completion;
        await worker.StopAsync(CancellationToken.None);

        return transport.Entries.Single();
    }

    // Builds the event exactly as Sl4nLogger does: message pre-formatted with RAW values
    // (MEL's formatter) + the structured state carrying {OriginalFormat}.
    private static RawLogEvent MelEvent(string formatted, string template, params (string Key, object? Value)[] state)
    {
        List<KeyValuePair<string, object?>> s =
            state.Select(f => KeyValuePair.Create(f.Key, f.Value)).ToList();
        s.Add(KeyValuePair.Create("{OriginalFormat}", (object?)template));
        return new RawLogEvent(LogLevel.Information, "Test", formatted, s, null, null);
    }

    [Fact]
    public async Task TemplateInterpolatedPII_IsMaskedInsideTheMessage()
    {
        // The README quick start, verbatim.
        Dictionary<string, object?> entry = await RunOne(MelEvent(
            formatted: "Card charged 299.9 for john@example.com",   // what MEL's formatter produced (RAW)
            template:  "Card charged {Amount} for {Email}",
            ("Amount", 299.9), ("Email", "john@example.com")));

        entry["message"].Should().Be("Card charged 299.9 for j**n@example.com");
        entry["Email"].Should().Be("j**n@example.com");
        entry["Amount"].Should().Be(299.9);
        entry["message"].As<string>().Should().NotContain("john@example.com");
    }

    [Fact]
    public async Task NoMaskableKeys_MessageIsKeptByteIdentical()
    {
        // MEL formatting fidelity (e.g. a format specifier) is preserved when nothing masks.
        Dictionary<string, object?> entry = await RunOne(MelEvent(
            formatted: "Charged 299.90 for order A-42",             // ":0.00" applied by MEL
            template:  "Charged {Amount:0.00} for order {OrderId}",
            ("Amount", 299.9), ("OrderId", "A-42")));

        entry["message"].Should().Be("Charged 299.90 for order A-42");
    }

    [Fact]
    public async Task BraceEscapes_AndUnknownTokens_SurviveReRendering()
    {
        Dictionary<string, object?> entry = await RunOne(MelEvent(
            formatted: "{json} hunter2 {Missing}",
            template:  "{{json}} {password} {Missing}",
            ("password", "hunter2")));

        entry["message"].Should().Be("{json} ******* {Missing}");
        entry["password"].Should().Be("*******");
    }
}
