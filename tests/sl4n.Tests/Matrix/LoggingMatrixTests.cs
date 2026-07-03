using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

public sealed class LoggingMatrixTests
{
    // ── Not configured → allow all ────────────────────────────────────────────

    [Fact]
    public void Create_NullMap_ReturnsEmpty_AllowsEverything()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(null);

        matrix.Should().BeSameAs(LoggingMatrix.Empty);
        matrix.AllowedFields("information").Should().BeNull(); // null = allow all
    }

    [Fact]
    public void Create_EmptyMap_AllowsEverything()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>());

        matrix.AllowedFields("error").Should().BeNull();
    }

    // ── Per-level whitelist ───────────────────────────────────────────────────

    [Fact]
    public void AllowedFields_ReturnsLevelWhitelist()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["information"] = ["correlationId", "userId"],
        });

        matrix.AllowedFields("information").Should().BeEquivalentTo("correlationId", "userId");
    }

    [Fact]
    public void AllowedFields_Wildcard_AllowsEverything()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["error"] = ["*"],
        });

        matrix.AllowedFields("error").Should().BeNull(); // "*" resolves to allow-all
    }

    // ── default fallback ──────────────────────────────────────────────────────

    [Fact]
    public void AllowedFields_UnlistedLevel_FallsBackToDefault()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["default"] = ["correlationId"],
            ["error"]   = ["*"],
        });

        matrix.AllowedFields("information").Should().BeEquivalentTo("correlationId");
    }

    [Fact]
    public void AllowedFields_ConfiguredButNoLevelAndNoDefault_DropsAllContext()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["error"] = ["correlationId"],
        });

        // configured, but "information" isn't listed and there's no default → strict whitelist
        matrix.AllowedFields("information").Should().NotBeNull();
        matrix.AllowedFields("information").Should().BeEmpty();
    }

    // ── Case-insensitivity ────────────────────────────────────────────────────

    [Fact]
    public void AllowedFields_LevelLookup_IsCaseInsensitive()
    {
        LoggingMatrix matrix = LoggingMatrix.Create(new Dictionary<string, string[]>
        {
            ["Information"] = ["correlationId"],
        });

        matrix.AllowedFields("information").Should().BeEquivalentTo("correlationId");
    }
}
