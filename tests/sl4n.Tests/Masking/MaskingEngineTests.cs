using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

public sealed class MaskingEngineTests
{
    private static readonly MaskingEngine _engine =
        MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

    private static IReadOnlyDictionary<string, object?> Apply(params (string Key, object? Value)[] fields)
    {
        IEnumerable<KeyValuePair<string, object?>> state =
            fields.Select(f => KeyValuePair.Create(f.Key, f.Value));
        return _engine.Apply(state).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    // ── Email ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("email")]
    [InlineData("mail")]
    [InlineData("EMAIL")]
    public void Email_Field_IsMasked(string fieldName)
    {
        Apply((fieldName, "john@example.com"))[fieldName]
            .Should().Be("j**n@example.com");
    }

    [Fact]
    public void Email_PreservesFirstLastCharAndDomain()
    {
        Apply(("email", "john.doe@example.com"))["email"]
            .Should().Be("j******e@example.com");  // john.doe = 8 chars → j + 6 * + e
    }

    // ── FullMask ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("password")]
    [InlineData("pass")]
    [InlineData("pwd")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("key")]
    [InlineData("auth")]
    [InlineData("jwt")]
    [InlineData("bearer")]
    public void FullMask_Field_IsFullyMasked(string fieldName)
    {
        string value = "super-secret-value";
        Apply((fieldName, value))[fieldName]
            .Should().Be(new string('*', value.Length));
    }

    // ── LastFour ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("credit_card")]
    [InlineData("creditcard")]
    [InlineData("card_number")]
    [InlineData("cardnumber")]
    public void CreditCard_Field_ShowsLastFour(string fieldName)
    {
        Apply((fieldName, "4111111111111234"))[fieldName]
            .Should().Be("************1234");
    }

    [Theory]
    [InlineData("ssn")]
    [InlineData("social_security")]
    public void Ssn_Field_ShowsLastFour(string fieldName)
    {
        Apply((fieldName, "123456789"))[fieldName]
            .Should().Be("*****6789");
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("mobile")]
    [InlineData("tel")]
    public void Phone_Field_ShowsLastFour(string fieldName)
    {
        Apply((fieldName, "5551234567"))[fieldName]
            .Should().Be("******4567");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void UnknownField_IsNotMasked()
    {
        Apply(("amount", 299.90m))["amount"]
            .Should().Be(299.90m);
    }

    [Fact]
    public void NullValue_RemainsNull()
    {
        Apply(("email", null))["email"]
            .Should().BeNull();
    }

    [Fact]
    public void MultipleFields_EachMaskedIndependently()
    {
        IReadOnlyDictionary<string, object?> result = Apply(
            ("email",    "john@example.com"),
            ("amount",   299.90m),
            ("password", "secret123"));

        result["email"].Should().Be("j**n@example.com");
        result["amount"].Should().Be(299.90m);
        result["password"].Should().Be(new string('*', "secret123".Length));
    }

    [Fact]
    public void NoRules_NothingIsMasked()
    {
        MaskingEngine emptyEngine = MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = false });
        IEnumerable<KeyValuePair<string, object?>> state =
        [
            KeyValuePair.Create<string, object?>("email", "john@example.com")
        ];

        emptyEngine.Apply(state).First().Value
            .Should().Be("john@example.com");
    }

    // ── Custom rule ───────────────────────────────────────────────────────────

    [Fact]
    public void CustomRule_IsApplied()
    {
        MaskingRule customRule = new MaskingRule(
            MaskingPatterns.EmailField(),
            MaskingStrategy.Custom,
            value => "[REDACTED]");

        MaskingEngine engineWithCustom = new MaskingEngine([customRule]);

        IEnumerable<KeyValuePair<string, object?>> state =
        [
            KeyValuePair.Create<string, object?>("email", "john@example.com")
        ];

        engineWithCustom.Apply(state).First().Value
            .Should().Be("[REDACTED]");
    }

    // ── Custom rules via config ─────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?> ApplyWith(
        MaskingEngine engine, params (string Key, object? Value)[] fields) =>
        engine.Apply(fields.Select(f => KeyValuePair.Create(f.Key, f.Value)))
              .ToDictionary(kv => kv.Key, kv => kv.Value);

    [Fact]
    public void ConfigRule_CustomPattern_IsApplied()
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig
        {
            EnableDefaultRules = false,
            Rules = { new MaskingRuleConfig { Pattern = "^(cvv|cvc)$", Strategy = MaskingStrategy.FullMask } },
        });

        ApplyWith(engine, ("cvv", "123"))["cvv"].Should().Be("***");
    }

    [Fact]
    public void ConfigRule_IsAppendedOnTopOfDefaults()
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig
        {
            EnableDefaultRules = true,
            Rules = { new MaskingRuleConfig { Pattern = "^internalId$", Strategy = MaskingStrategy.FullMask } },
        });

        IReadOnlyDictionary<string, object?> r = ApplyWith(engine,
            ("email", "john@example.com"), ("internalId", "abc123"));

        r["email"].Should().Be("j**n@example.com");        // default rule still applies
        r["internalId"].Should().Be(new string('*', 6));   // custom rule applies too
    }

    [Fact]
    public void ConfigRule_UsingMaskKeys_BuildsWorkingPattern()
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig
        {
            EnableDefaultRules = false,
            Rules = { new MaskingRuleConfig { Pattern = MaskKeys.Pattern(MaskKeys.Token), Strategy = MaskingStrategy.FullMask } },
        });

        ApplyWith(engine, ("jwt", "header.payload.sig"))["jwt"]
            .Should().Be(new string('*', "header.payload.sig".Length));
    }

    // ── Non-string redaction ────────────────────────────────────────────────────

    [Fact]
    public void NonStringValue_UnderSensitiveKey_IsFullyRedacted()
    {
        // A nested object under a sensitive key must not leak — it is redacted, not stringified.
        object nested = new Dictionary<string, object?> { ["number"] = "4111111111111234" };
        Apply(("password", nested))["password"].Should().Be("[REDACTED]");
        Apply(("token", 123456))["token"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void NonStringValue_UnderNonSensitiveKey_PassesThrough()
    {
        Apply(("amount", 299.90m))["amount"].Should().Be(299.90m);
    }

    // ── Silent observer — masking never throws ─────────────────────────────────

    [Fact]
    public void CustomMaskThatThrows_IsRedacted_AndReported()
    {
        Exception? captured = null;
        string? capturedKey = null;
        MaskingRule throwing = new(
            MaskingPatterns.EmailField(),
            MaskingStrategy.Custom,
            _ => throw new InvalidOperationException("boom"));
        MaskingEngine engine = new([throwing], (ex, key) => { captured = ex; capturedKey = key; });

        ApplyWith(engine, ("email", "john@example.com"))["email"].Should().Be("[REDACTED]");
        captured.Should().BeOfType<InvalidOperationException>();
        capturedKey.Should().Be("email");
    }

    [Fact]
    public void RegexTimeout_IsRedacted_AndReported()
    {
        // Catastrophic-backtracking pattern + a 1-tick timeout → the ReDoS guard trips deterministically.
        Exception? captured = null;
        Regex pathological = new("^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        MaskingEngine engine = new([new MaskingRule(pathological, MaskingStrategy.FullMask)],
            (ex, _) => captured = ex);

        string key = new string('a', 32) + "!";
        ApplyWith(engine, (key, "value"))[key].Should().Be("[REDACTED]");
        captured.Should().BeOfType<RegexMatchTimeoutException>();
    }

    // ── Decision cache (perf fix, 2026-07-11 — semantics must be identical) ────

    [Fact]
    public void Cache_RepeatCalls_MaskIdentically_RuleCachedNotValue()
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

        ApplyWith(engine, ("email", "john@example.com"))["email"].Should().Be("j**n@example.com");
        ApplyWith(engine, ("email", "anna@example.com"))["email"].Should().Be("a**a@example.com");
        ApplyWith(engine, ("name", "John"))["name"].Should().Be("John");
        ApplyWith(engine, ("name", "Anna"))["name"].Should().Be("Anna");

        engine.DecisionCacheSize.Should().Be(2); // email + name, one entry per key NAME
    }

    [Fact]
    public void Cache_IsBounded_NewKeysStillMaskCorrectly_Uncached()
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

        for (int i = 0; i < MaskingEngine.DecisionCacheMax + 50; i++)
            _ = ApplyWith(engine, ($"field_{i}", "v"));

        engine.DecisionCacheSize.Should().Be(MaskingEngine.DecisionCacheMax);

        // A brand-new sensitive key AFTER the cap still masks (pays the scan, isn't stored).
        ApplyWith(engine, ("password", "hunter2"))["password"].Should().Be("*******");
        engine.DecisionCacheSize.Should().Be(MaskingEngine.DecisionCacheMax);
    }

    [Fact]
    public void Cache_RegexTimeout_IsTransient_AndNeverCached()
    {
        // A ReDoS timeout mid-scan must NOT poison the decision for that key: the failure is
        // transient (fail-secure Redacted), so the key stays out of the cache while other
        // keys keep caching. Same contract as the Java sibling. A 1-tick timeout trips on ANY
        // evaluation of that rule (the guard checks the clock on entry, not only when
        // backtracking) — which is exactly what makes this test deterministic.
        Regex pathological = new("^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        MaskingEngine engine = new([
            new MaskingRule(MaskingPatterns.EmailField(), MaskingStrategy.Email), // safe, no timeout
            new MaskingRule(pathological, MaskingStrategy.FullMask),
        ]);

        string hostile = new string('a', 32) + "!";
        ApplyWith(engine, (hostile, "value"))[hostile].Should().Be("[REDACTED]");
        engine.DecisionCacheSize.Should().Be(0); // the timeout was not cached

        // 'email' matches the safe FIRST rule — the pathological one is never consulted
        // (first match wins), so this key caches normally.
        ApplyWith(engine, ("email", "john@example.com"))["email"].Should().Be("j**n@example.com");
        engine.DecisionCacheSize.Should().Be(1);
    }

    // ── MaskOne is Apply, one pair at a time ─────────────────────────────────────────
    // The worker builds a masked and an unmasked projection from a SINGLE pass over the
    // state, so it masks per key instead of calling Apply. If the two ever disagreed,
    // exempting one sink would quietly change what every other sink receives.

    [Theory]
    [InlineData("email", "john@example.com")]      // rule matches, string
    [InlineData("password", "hunter2")]            // rule matches, different strategy
    [InlineData("userId", "abc-123")]              // no rule
    [InlineData("Email", "not-an-email")]          // rule matches, value doesn't parse
    [InlineData("email", null)]                    // null under a matching rule
    [InlineData("token", "")]                      // empty under a matching rule
    public void MaskOne_MatchesApply_PairForPair(string key, object? value)
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });

        object? viaApply = engine
            .Apply([KeyValuePair.Create(key, value)])
            .Single().Value;

        engine.MaskOne(key, value).Should().Be(viaApply);
    }

    [Fact]
    public void MaskOne_MatchesApply_ForNonStringUnderASensitiveKey()
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = true });
        object?[] values = [42, 3.5, true, new { a = 1 }, new[] { 1, 2 }];

        foreach (object? v in values)
        {
            object? viaApply = engine.Apply([KeyValuePair.Create<string, object?>("password", v)])
                                     .Single().Value;
            engine.MaskOne("password", v).Should().Be(viaApply);
        }
    }

    [Fact]
    public void MaskOne_WithNoRules_ReturnsTheValueUntouched_LikeApply()
    {
        MaskingEngine engine = MaskingEngine.Create(new MaskingConfig { EnableDefaultRules = false });

        engine.MaskOne("email", "john@example.com").Should().Be("john@example.com");
        engine.DecisionCacheSize.Should().Be(0); // no rules ⇒ no scan, nothing cached
    }
}
