using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sl4n.Tests;

/// <summary>
/// A domain write path that persists the retention class in its own column must get the same answer
/// the logger got, from the same registry, without going through a logger to ask.
/// </summary>
public sealed class RetentionResolutionTests
{
    private static RetentionRegistry Registry() => RetentionRegistry.Create(
        new Dictionary<string, RetentionPolicy>
        {
            ["SOX_AUDIT_TRAIL"] = new() { Years = 7,  Class = "SOX" },
            ["GDPR_STANDARD"]   = new() { Months = 6, Class = "GDPR" },
            ["OPS_SHORT"]       = new() { Days = 30,  Class = "ops" },
        });

    // ── The registry is injectable, which is the whole point ─────────────────────

    [Fact]
    public void Registry_IsResolvableFromDi_WithoutTouchingALogger()
    {
        ServiceCollection services = new();
        services.AddSl4n(cfg => cfg.RetentionPolicies = new Dictionary<string, RetentionPolicy>
        {
            ["SOX_AUDIT_TRAIL"] = new() { Years = 7, Class = "SOX" },
        });

        RetentionRegistry registry = services.BuildServiceProvider()
            .GetRequiredService<RetentionRegistry>();

        registry.Resolve("SOX_AUDIT_TRAIL").Class.Should().Be("SOX");
    }

    [Fact]
    public void Registry_AnswersTheSameAsTheLoggingPath()
    {
        RetentionRegistry registry = Registry();
        DateOnly at = new(2026, 8, 23);

        // Same registry, same arithmetic, same answer — whichever way it is asked. If these ever
        // diverged, a record's own column and its log line would disagree about when it expires.
        registry.Until("SOX_AUDIT_TRAIL", new DateTimeOffset(at, TimeOnly.MinValue, TimeSpan.Zero))
                .Should().Be(new DateOnly(2033, 8, 23));
    }

    // ── Loud on a miss ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_UnknownName_ThrowsAndSaysWhatIsAvailable()
    {
        Action resolve = () => Registry().Resolve("SOX_AUDIT_TRAL"); // typo

        // A silent null here means a record persisted with no retention at all, discovered at audit.
        resolve.Should().Throw<RetentionPolicyNotFoundException>()
               .Which.Message.Should().Contain("SOX_AUDIT_TRAL")
               .And.Contain("GDPR_STANDARD");  // the available names, to make the typo obvious
    }

    [Fact]
    public void Resolve_UnknownName_CarriesTheNameAndTheAvailableOnes()
    {
        RetentionPolicyNotFoundException ex = Assert.Throws<RetentionPolicyNotFoundException>(
            () => Registry().Resolve("NOPE"));

        ex.PolicyName.Should().Be("NOPE");
        ex.AvailablePolicies.Should().BeEquivalentTo(["GDPR_STANDARD", "OPS_SHORT", "SOX_AUDIT_TRAIL"]);
        ex.AvailablePolicies.Should().BeInAscendingOrder(); // sorted, so the message is stable
    }

    [Fact]
    public void Until_UnknownName_ThrowsToo()
    {
        Action until = () => Registry().Until("NOPE", DateTimeOffset.UnixEpoch);
        until.Should().Throw<RetentionPolicyNotFoundException>();
    }

    [Fact]
    public void TryResolve_StaysQuiet_ForCallersThatWantToBranch()
    {
        Registry().TryResolve("NOPE", out RetentionPolicy? policy).Should().BeFalse();
        policy.Should().BeNull();
    }

    // ── The frozen registry ──────────────────────────────────────────────────────

    [Fact]
    public void Policies_ExposesEverythingRegistered()
    {
        Registry().Policies.Keys.Should().BeEquivalentTo(
            ["SOX_AUDIT_TRAIL", "GDPR_STANDARD", "OPS_SHORT"]);
    }

    [Fact]
    public void Policies_CannotBeMutatedThroughTheExposedView()
    {
        RetentionRegistry registry = Registry();

        // Handing out the live dictionary would let a consumer redefine a compliance window at
        // runtime — for records already written under the old one.
        // IReadOnlyDictionary alone does NOT stop this — the interface hides the mutators, the cast
        // brings them back. The registry has to be genuinely read-only, not read-only by convention.
        Action mutate = () => ((IDictionary<string, RetentionPolicy>)registry.Policies)
            .Add("SNEAKY", new RetentionPolicy { Days = 1 });
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Policies_AreCopied_SoTheCallersDictionaryCannotChangeThemLater()
    {
        Dictionary<string, RetentionPolicy> source = new()
        {
            ["SOX_AUDIT_TRAIL"] = new() { Years = 7, Class = "SOX" },
        };
        RetentionRegistry registry = RetentionRegistry.Create(source);

        // The other way in: keep the dictionary you handed over and edit it afterwards. A window
        // that can be shortened after the fact is a window that means nothing.
        source["SOX_AUDIT_TRAIL"] = new RetentionPolicy { Days = 1, Class = "oops" };
        source["ADDED_LATER"]     = new RetentionPolicy { Days = 1 };

        registry.Resolve("SOX_AUDIT_TRAIL").Years.Should().Be(7);
        registry.Resolve("SOX_AUDIT_TRAIL").Class.Should().Be("SOX");
        registry.Policies.Should().NotContainKey("ADDED_LATER");
    }

    [Fact]
    public void Policies_LookupIsCaseInsensitive_LikeTheLoggingPath()
    {
        Registry().Resolve("sox_audit_trail").Class.Should().Be("SOX");
    }

    // ── No unit declared ─────────────────────────────────────────────────────────

    [Fact]
    public void Until_PolicyWithNoUnit_ReturnsNull_ButResolveStillSucceeds()
    {
        RetentionRegistry registry = RetentionRegistry.Create(
            new Dictionary<string, RetentionPolicy> { ["TAG_ONLY"] = new() { Class = "unclassified" } });

        registry.Resolve("TAG_ONLY").Class.Should().Be("unclassified");
        registry.Until("TAG_ONLY", DateTimeOffset.UnixEpoch).Should().BeNull();
    }

    [Fact]
    public void Until_UsesUtc_LikeTheWorker()
    {
        // 23:30 at UTC+3 is already the next day in UTC.
        Registry().Until("OPS_SHORT", new DateTimeOffset(2026, 8, 23, 23, 30, 0, TimeSpan.FromHours(3)))
                  .Should().Be(new DateOnly(2026, 9, 22));
    }

    [Fact]
    public void Empty_ResolvesNothing_AndSaysSo()
    {
        Action resolve = () => RetentionRegistry.Empty.Resolve("ANY");
        resolve.Should().Throw<RetentionPolicyNotFoundException>();
        RetentionRegistry.Empty.Policies.Should().BeEmpty();
    }
}
