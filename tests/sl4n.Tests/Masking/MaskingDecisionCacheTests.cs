using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

/// <summary>
/// The per-key-name decision cache (key → matched rule, or none). Field names repeat
/// across entries while values change, so the DECISION is cached — never the value.
/// Family fix ported from SyntropyLog JS 1.4.0 (2.4× masking there).
/// </summary>
public sealed class MaskingDecisionCacheTests
{
    private static MaskingEngine NewEngine(Action<Exception, string>? onError = null) =>
        MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true }, onError);

    private static object? Mask(MaskingEngine engine, string key, object? value) =>
        engine.Apply(new[] { KeyValuePair.Create(key, value) }).Single().Value;

    [Fact]
    public void RepeatKeys_MaskIdentically_AndPopulateTheCache()
    {
        MaskingEngine engine = NewEngine();

        object? first  = Mask(engine, "password", "hunter2");
        object? second = Mask(engine, "password", "hunter2");   // cached decision this time

        second.Should().Be(first).And.Be("*******");            // deterministic — cache changes nothing
        engine.DecisionCacheSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NoRuleKeys_CacheTheMissToo_AndStayUnmasked()
    {
        MaskingEngine engine = NewEngine();

        Mask(engine, "orderId", "A-42").Should().Be("A-42");
        int size = engine.DecisionCacheSize;

        Mask(engine, "orderId", "B-43").Should().Be("B-43");    // "no rule" decision reused
        engine.DecisionCacheSize.Should().Be(size);
    }

    [Fact]
    public void PastTheCap_NewKeysStillMaskCorrectly_Uncached()
    {
        MaskingEngine engine = NewEngine();

        for (int i = 0; i < 5000; i++)                          // exceed the 4096 cap
            Mask(engine, "field_" + i, "x");

        engine.DecisionCacheSize.Should().BeLessThanOrEqualTo(4096);
        Mask(engine, "password", "hunter2").Should().Be("*******"); // cache full → scan path, still masked
    }

    [Fact]
    public void RegexTimeout_IsNotCached_AndStaysFailSecure()
    {
        // A pathological custom pattern against a long non-matching key forces the
        // (real, enforced) match timeout. The decision must NOT be cached — the timeout
        // is transient — and the value must redact, never pass through.
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
        Mask(engine, hostileKey, "value").Should().Be("[REDACTED]"); // fail-secure
        reported.Should().ContainSingle().Which.Should().Be(hostileKey);
        engine.DecisionCacheSize.Should().Be(0);                     // transient → not cached
    }
}
