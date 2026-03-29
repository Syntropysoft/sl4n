namespace Sl4n;

/// <summary>
/// Optional user-land constants to avoid magic strings.
/// The framework does not reference this class — it is field-agnostic.
/// </summary>
public static class Sl4nFields
{
    public const string CorrelationId = "correlationId";
    public const string TraceId       = "traceId";
    public const string TenantId      = "tenantId";

    public static class Sources
    {
        public const string Frontend = "frontend";
        public const string Partner  = "partner";
    }

    public static class Targets
    {
        public const string Http  = "http";
        public const string Kafka = "kafka";
        public const string S3    = "s3";
    }
}
