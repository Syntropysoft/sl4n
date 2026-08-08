using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

/// <summary>
/// <see cref="MaskingEngine.HasRuleFor"/> — the public cached-decision lookup the worker
/// uses to decide whether to re-render the message from masked values (Phase 7.5).
/// Cache mechanics themselves (population, cap, timeout-not-cached) are covered by the
/// decision-cache suite in MaskingEngineTests.
/// </summary>
public sealed class MaskingDecisionCacheTests
{
    private static object? Mask(MaskingEngine engine, string key, object? value) =>
        engine.Apply(new[] { KeyValuePair.Create(key, value) }).Single().Value;

    [Fact]
    public void HasRuleFor_TrueForRuledKey_FalseForUnruledKey()
    {
        MaskingEngine engine =
            MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

        engine.HasRuleFor("password").Should().BeTrue();
        engine.HasRuleFor("orderId").Should().BeFalse();
    }

    [Fact]
    public void HasRuleFor_SharesTheDecisionCacheWithMasking()
    {
        MaskingEngine engine =
            MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

        Mask(engine, "email", "a@b.c");                    // MaskValue caches the decision
        int size = engine.DecisionCacheSize;

        engine.HasRuleFor("email").Should().BeTrue();      // pure cache hit
        engine.DecisionCacheSize.Should().Be(size);        // no new entry
    }

    [Fact]
    public void HasRuleFor_RegexTimeout_AnswersTrueFailSecure_AndIsNotCached()
    {
        List<string> reported = new();
        MaskingEngine engine = MaskingEngine.Create(
            new MaskingConfig
            {
                EnableDefaultRules = false,
                RegexTimeoutMs     = 1,
                Rules = { new MaskingRuleConfig { Pattern = "(a+)+$", Strategy = MaskingStrategy.FullMask } },
            },
            (_, key) => reported.Add(key));

        string hostileKey = new string('a', 40) + "!";

        engine.HasRuleFor(hostileKey).Should().BeTrue();   // fail-secure: assume sensitive
        reported.Should().ContainSingle().Which.Should().Be(hostileKey);
        engine.DecisionCacheSize.Should().Be(0);           // transient → not cached
    }
}
