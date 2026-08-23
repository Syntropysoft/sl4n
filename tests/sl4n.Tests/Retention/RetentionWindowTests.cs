using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

/// <summary>
/// The window must never end EARLY. .NET's own AddMonths/AddYears clamp to the last day of the
/// target month, which shortens every edge-case window by one to three days — the exact failure a
/// retention policy exists to prevent. These tests pin the override, the way the JS sibling pins
/// its rollover.
/// </summary>
public sealed class RetentionWindowTests
{
    private static DateOnly Until(DateOnly at, int days = 0, int months = 0, int years = 0) =>
        RetentionWindow.Until(at, new RetentionPolicy { Days = days, Months = months, Years = years })!.Value;

    // ── The rounding override ────────────────────────────────────────────────────────

    [Fact]
    public void AddMonths_RollsForward_NeverShorteningTheWindow()
    {
        // .NET gives 2026-02-28 here. That is three days SHORT, and short is the failure an
        // auditor punishes — so the window rolls into March instead.
        new DateOnly(2026, 1, 31).AddMonths(1).Should().Be(new DateOnly(2026, 2, 28)); // what .NET does
        Until(new DateOnly(2026, 1, 31), months: 1).Should().Be(new DateOnly(2026, 3, 3));
    }

    [Fact]
    public void AddYears_RollsForward_OnALeapDay()
    {
        new DateOnly(2024, 2, 29).AddYears(7).Should().Be(new DateOnly(2031, 2, 28)); // what .NET does
        Until(new DateOnly(2024, 2, 29), years: 7).Should().Be(new DateOnly(2031, 3, 1));
    }

    [Fact]
    public void AddMonths_RollsForward_OnALeapDay()
    {
        Until(new DateOnly(2024, 2, 29), months: 12).Should().Be(new DateOnly(2025, 3, 1));
    }

    [Theory]
    [InlineData(2026, 3, 31, 2026, 5, 1)]   // 31-Mar + 1m → .NET says 30-Apr; we roll to 1-May
    [InlineData(2026, 5, 31, 2026, 7, 1)]   // 31-May + 1m → .NET says 30-Jun; we roll to 1-Jul
    [InlineData(2026, 8, 31, 2026, 10, 1)]  // 31-Aug + 1m → .NET says 30-Sep; we roll to 1-Oct
    public void AddMonths_RollsForward_OnEveryShortTargetMonth(
        int y, int m, int d, int ey, int em, int ed)
    {
        Until(new DateOnly(y, m, d), months: 1).Should().Be(new DateOnly(ey, em, ed));
    }

    [Fact]
    public void AddMonths_DoesNotRoll_WhenTheTargetDayExists()
    {
        // No clamping happened, so there is nothing to correct — a spurious roll would be its own bug.
        Until(new DateOnly(2026, 4, 30), months: 1).Should().Be(new DateOnly(2026, 5, 30));
        Until(new DateOnly(2026, 1, 15), months: 1).Should().Be(new DateOnly(2026, 2, 15));
        Until(new DateOnly(2026, 1, 28), months: 1).Should().Be(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void Days_AreExact_AndNeedNoOverride()
    {
        Until(new DateOnly(2026, 1, 31), days: 30).Should().Be(new DateOnly(2026, 3, 2));
    }

    // ── Why declaring the unit matters ───────────────────────────────────────────────

    [Fact]
    public void SevenYearsInDays_EndsEarlierThanSevenYears_WhichIsWhyTheUnitExists()
    {
        DateOnly at = new(2026, 8, 23);

        // 2555 = 7 × 365, the value the README's own SOX example carries. It ignores leap days.
        DateOnly byDays  = Until(at, days: 2555);
        DateOnly byYears = Until(at, years: 7);

        byDays.Should().Be(new DateOnly(2033, 8, 21));
        byYears.Should().Be(new DateOnly(2033, 8, 23));
        byDays.Should().BeBefore(byYears);
    }

    // ── No unit declared ─────────────────────────────────────────────────────────────

    [Fact]
    public void NoUnitDeclared_ReturnsNull_RatherThanInventingADate()
    {
        RetentionWindow.Until(new DateOnly(2026, 8, 23), new RetentionPolicy { Class = "SOX" })
            .Should().BeNull();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -3, 0)]
    [InlineData(0, 0, -7)]
    public void NegativeWindow_ReturnsNull_RatherThanADateInThePast(int d, int m, int y)
    {
        RetentionWindow.Until(new DateOnly(2026, 8, 23),
            new RetentionPolicy { Days = d, Months = m, Years = y }).Should().BeNull();
    }

    // ── The emitted form ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-AR")]
    [InlineData("de-DE")]
    public void Iso_IsTheSame_WhateverTheServerCultureIs(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            RetentionWindow.ToIso(new DateOnly(2033, 8, 23)).Should().Be("2033-08-23");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
