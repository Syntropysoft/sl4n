using System.Collections.Immutable;
using FluentAssertions;
using Xunit;

namespace Sl4n.Tests;

public sealed class Sl4nContextTests
{
    // ── Push / Dispose ────────────────────────────────────────────────────────

    [Fact]
    public void Push_StoresFields_InCurrent()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"), ("userId", "usr-42"));

        Sl4nContext.Current["correlationId"].Should().Be("req-001");
        Sl4nContext.Current["userId"].Should().Be("usr-42");
    }

    [Fact]
    public void Push_Dispose_RestoresPreviousState()
    {
        using (Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001")))
        {
            Sl4nContext.Current.ContainsKey("correlationId").Should().BeTrue();
        }

        Sl4nContext.Current.ContainsKey("correlationId").Should().BeFalse();
    }

    [Fact]
    public void Push_NestedScopes_RestoredInOrder()
    {
        using (Sl4nScope outer = Sl4nContext.Push(("correlationId", "outer")))
        {
            using (Sl4nScope inner = Sl4nContext.Push(("correlationId", "inner")))
            {
                Sl4nContext.Current["correlationId"].Should().Be("inner");
            }

            Sl4nContext.Current["correlationId"].Should().Be("outer");
        }

        Sl4nContext.Current.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Push_ChildTask_InheritsParentContext()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        string? valueInChild = await Task.Run(() =>
            Sl4nContext.Current.GetValueOrDefault("correlationId")?.ToString());

        valueInChild.Should().Be("req-001");
    }

    [Fact]
    public async Task Push_ChildTask_MutationDoesNotLeakToParent()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        await Task.Run(() => Sl4nContext.Set("correlationId", "mutated-in-child"));

        Sl4nContext.Current["correlationId"].Should().Be("req-001");
    }

    [Fact]
    public void Push_WithDictionary_StoresAllFields()
    {
        IReadOnlyDictionary<string, string> fields = new Dictionary<string, string>
        {
            ["correlationId"] = "req-001",
            ["traceId"]       = "trace-xyz"
        };

        using Sl4nScope scope = Sl4nContext.Push(fields);

        Sl4nContext.Current["correlationId"].Should().Be("req-001");
        Sl4nContext.Current["traceId"].Should().Be("trace-xyz");
    }

    // ── Set ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Set_AddsField_ToCurrentContext()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        Sl4nContext.Set("userId", "usr-42");

        Sl4nContext.Current["userId"].Should().Be("usr-42");
        Sl4nContext.Current["correlationId"].Should().Be("req-001");
    }

    [Fact]
    public void Set_UpdatesExistingField()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "old"));

        Sl4nContext.Set("correlationId", "new");

        Sl4nContext.Current["correlationId"].Should().Be("new");
    }

    // ── ExtractInbound ────────────────────────────────────────────────────────

    private static readonly ContextConfig _config = new()
    {
        Source = "frontend",
        Inbound = new()
        {
            ["frontend"] = new() { ["correlationId"] = "X-Correlation-ID", ["traceId"] = "X-Trace-ID" },
            ["partner"]  = new() { ["correlationId"] = "x-request-id" }
        }
    };

    [Fact]
    public void ExtractInbound_MapsWireNamesToInternalFields()
    {
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            ["x-correlation-id"] = "req-001",
            ["x-trace-id"]       = "trace-xyz"
        };

        Sl4nContext.ExtractInbound(headers, "frontend", _config)
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["correlationId"] = "req-001",
                ["traceId"]       = "trace-xyz"
            });
    }

    [Fact]
    public void ExtractInbound_WireNamesMatchedCaseInsensitive_AgainstLowercasedHeaders()
    {
        // The middleware normalizes headers to lowercase before calling ExtractInbound.
        // Config declares "X-Correlation-ID" — the function lowercases it to "x-correlation-id"
        // and looks it up in the pre-normalized headers dictionary.
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            ["x-correlation-id"] = "req-001"   // already lowercased, as the middleware does
        };

        Sl4nContext.ExtractInbound(headers, "frontend", _config)
            .Should().ContainKey("correlationId").WhoseValue.Should().Be("req-001");
    }

    [Fact]
    public void ExtractInbound_MissingHeader_OmitsField()
    {
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            ["x-correlation-id"] = "req-001"
            // x-trace-id missing
        };

        IReadOnlyDictionary<string, string> result =
            Sl4nContext.ExtractInbound(headers, "frontend", _config);

        result.Should().ContainKey("correlationId");
        result.Should().NotContainKey("traceId");
    }

    [Fact]
    public void ExtractInbound_UnknownSource_ReturnsEmpty()
    {
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            ["x-correlation-id"] = "req-001"
        };

        Sl4nContext.ExtractInbound(headers, "unknown-source", _config)
            .Should().BeEmpty();
    }

    [Fact]
    public void ExtractInbound_EmptyHeaders_ReturnsEmpty()
    {
        Sl4nContext.ExtractInbound(ImmutableDictionary<string, string>.Empty, "frontend", _config)
            .Should().BeEmpty();
    }

    // ── GetPropagationHeaders ─────────────────────────────────────────────────

    private static readonly ContextConfig _outboundConfig = new()
    {
        Outbound = new()
        {
            ["http"]  = new() { ["correlationId"] = "X-Correlation-ID", ["traceId"] = "X-Trace-ID" },
            ["kafka"] = new() { ["correlationId"] = "correlationId" }
        }
    };

    [Fact]
    public void GetPropagationHeaders_EmptyContext_ReturnsEmpty()
    {
        Sl4nContext.GetPropagationHeaders("http", _outboundConfig)
            .Should().BeEmpty();
    }

    [Fact]
    public void GetPropagationHeaders_UnknownTarget_ReturnsEmpty()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        Sl4nContext.GetPropagationHeaders("unknown-target", _outboundConfig)
            .Should().BeEmpty();
    }

    [Fact]
    public void GetPropagationHeaders_MapsInternalFieldsToWireNames()
    {
        using Sl4nScope scope = Sl4nContext.Push(
            ("correlationId", "req-001"),
            ("traceId",       "trace-xyz"));

        Sl4nContext.GetPropagationHeaders("http", _outboundConfig)
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["X-Correlation-ID"] = "req-001",
                ["X-Trace-ID"]       = "trace-xyz"
            });
    }

    [Fact]
    public void GetPropagationHeaders_FieldsMissingFromContext_OmittedFromHeaders()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));
        // traceId not in context

        IReadOnlyDictionary<string, string> result =
            Sl4nContext.GetPropagationHeaders("http", _outboundConfig);

        result.Should().ContainKey("X-Correlation-ID");
        result.Should().NotContainKey("X-Trace-ID");
    }

    [Fact]
    public void GetPropagationHeaders_DifferentTargets_UseDifferentWireNames()
    {
        using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"));

        IReadOnlyDictionary<string, string> httpHeaders =
            Sl4nContext.GetPropagationHeaders("http", _outboundConfig);
        IReadOnlyDictionary<string, string> kafkaHeaders =
            Sl4nContext.GetPropagationHeaders("kafka", _outboundConfig);

        httpHeaders.Should().ContainKey("X-Correlation-ID").WhoseValue.Should().Be("req-001");
        kafkaHeaders.Should().ContainKey("correlationId").WhoseValue.Should().Be("req-001");
    }
}
