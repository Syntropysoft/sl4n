using System.Globalization;

namespace Sl4n;

/// <summary>
/// Turns a retention policy into the date its window ends. Pure and static: same inputs, same
/// answer, no clock and no state.
/// </summary>
internal static class RetentionWindow
{
    /// <summary>ISO-8601 calendar date — never the server's culture format.</summary>
    internal static string ToIso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The date <paramref name="policy"/>'s window ends, counted from <paramref name="at"/>.
    /// Null when no positive unit is declared — a missing window is reported as missing, never
    /// guessed. Exactly one unit is expected; <see cref="RetentionRegistry"/> rejects a policy
    /// declaring more than one at startup, so the order here is only a last-resort tiebreak.
    /// </summary>
    internal static DateOnly? Until(DateOnly at, RetentionPolicy policy)
    {
        if (policy.Years  > 0) return AddYearsRollingForward(at, policy.Years);
        if (policy.Months > 0) return AddMonthsRollingForward(at, policy.Months);
        if (policy.Days   > 0) return at.AddDays(policy.Days);
        return null;
    }

    /// <summary>True when the policy declares more than one unit, which is ambiguous.</summary>
    internal static bool HasAmbiguousUnit(RetentionPolicy policy) =>
        (policy.Days > 0 ? 1 : 0) + (policy.Months > 0 ? 1 : 0) + (policy.Years > 0 ? 1 : 0) > 1;

    // .NET clamps a short target month to its last day (31-Jan + 1 month → 28-Feb), which ends the
    // window EARLY — the one direction a compliance window must never round. Roll into the next
    // month instead, matching the JS sibling: long keeps the record, short deletes it too soon.
    private static DateOnly AddMonthsRollingForward(DateOnly at, int months) =>
        RollForward(at, at.AddMonths(months));

    private static DateOnly AddYearsRollingForward(DateOnly at, int years) =>
        RollForward(at, at.AddYears(years));

    private static DateOnly RollForward(DateOnly at, DateOnly shifted) =>
        shifted.Day == at.Day ? shifted : shifted.AddDays(at.Day - shifted.Day);
}
