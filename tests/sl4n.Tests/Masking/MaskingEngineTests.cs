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
}
