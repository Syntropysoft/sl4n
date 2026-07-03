using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

public sealed class SanitizerTests
{
    private const char ESC = (char)0x1B;
    private const char DEL = (char)0x7F;
    private const char NL  = (char)0x0A;
    private const char TAB = (char)0x09;
    private const char CR  = (char)0x0D;

    [Fact]
    public void CleanString_ReturnsSameReference()
    {
        string s = "plain value 123 - ok.";
        Sanitizer.Clean(s).Should().BeSameAs(s); // zero-allocation fast path
    }

    [Fact]
    public void EmptyString_ReturnsSame()
    {
        Sanitizer.Clean("").Should().Be("");
    }

    [Fact]
    public void StripsControlChars_IncludingNewlinesAndTabs()
    {
        Sanitizer.Clean("a" + NL + "b" + TAB + "c" + CR + "d").Should().Be("abcd");
    }

    [Fact]
    public void StripsDel()
    {
        Sanitizer.Clean("a" + DEL + "b").Should().Be("ab");
    }

    [Fact]
    public void StripsAnsiEscapeSequences()
    {
        Sanitizer.Clean(ESC + "[31mRED" + ESC + "[0m").Should().Be("RED");
    }

    [Fact]
    public void StripsLoneEscape()
    {
        Sanitizer.Clean("a" + ESC + "b").Should().Be("ab");
    }

    [Fact]
    public void LogInjectionAttempt_FakeLine_IsFlattened()
    {
        string evil = "ok" + NL + "{\"level\":\"error\",\"message\":\"faked\"}";
        string clean = Sanitizer.Clean(evil);
        clean.Should().NotContain(NL.ToString());
        clean.Should().StartWith("ok{");
    }
}
