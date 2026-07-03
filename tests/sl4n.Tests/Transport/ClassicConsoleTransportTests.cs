using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

public sealed class ClassicConsoleTransportTests
{
    private static string Render(Dictionary<string, object?> entry)
    {
        System.IO.TextWriter original = Console.Out;
        System.IO.StringWriter sw = new();
        Console.SetOut(sw);
        try { new ClassicConsoleTransport().Log(entry); }
        finally { Console.SetOut(original); }
        return sw.ToString().TrimEnd();
    }

    [Fact]
    public void Render_FormatsLevelCategoryMessageAndFields()
    {
        string line = Render(new Dictionary<string, object?>
        {
            ["level"]         = "information",
            ["category"]      = "OrdersService",
            ["message"]       = "Order created",
            ["correlationId"] = "req-001",
        });

        line.Should().Be("[INF] OrdersService: Order created correlationId=req-001");
    }

    [Fact]
    public void Render_IncludesTimestamp_WhenPresent()
    {
        string line = Render(new Dictionary<string, object?>
        {
            ["timestamp"] = "2026-06-20T10:00:00.0000000+00:00",
            ["level"]     = "error",
            ["category"]  = "X",
            ["message"]   = "boom",
        });

        line.Should().Be("2026-06-20T10:00:00.0000000+00:00 [ERR] X: boom");
    }

    [Theory]
    [InlineData("trace", "TRC")]
    [InlineData("debug", "DBG")]
    [InlineData("warning", "WRN")]
    [InlineData("critical", "CRT")]
    public void Render_AbbreviatesLevel(string level, string abbrev)
    {
        string line = Render(new Dictionary<string, object?>
        {
            ["level"] = level, ["category"] = "c", ["message"] = "m",
        });

        line.Should().Contain($"[{abbrev}]");
    }
}
