namespace Sl4n;

/// <summary>
/// Optional user-land constants to avoid magic strings.
/// The framework does not reference this class — it is field-agnostic.
/// </summary>
public static class Sl4nFields
{
    /// <summary>Conventional key for the request-scoped correlation id.</summary>
    public const string CorrelationId = "correlationId";

    /// <summary>Conventional key for a distributed trace id.</summary>
    public const string TraceId       = "traceId";

    /// <summary>Conventional key for the tenant a request belongs to.</summary>
    public const string TenantId      = "tenantId";

    /// <summary>Conventional names for inbound sources, as used in <see cref="ContextConfig.Inbound"/>.</summary>
    public static class Sources
    {
        /// <summary>Traffic arriving from your own frontend.</summary>
        public const string Frontend = "frontend";

        /// <summary>Traffic arriving from a third party.</summary>
        public const string Partner  = "partner";
    }

    /// <summary>Conventional names for outbound targets, as used in <see cref="ContextConfig.Outbound"/>.</summary>
    public static class Targets
    {
        /// <summary>Outbound HTTP calls.</summary>
        public const string Http  = "http";

        /// <summary>Messages published to Kafka.</summary>
        public const string Kafka = "kafka";

        /// <summary>Objects written to S3 or an S3-compatible store.</summary>
        public const string S3    = "s3";
    }
}
