namespace DisputePortal.SharedKernel.Web;

/// <summary>Names shared between the host, which defines policies, and the modules, which apply them.</summary>
public static class ApiConventions
{
    public const string CustomerPolicy = "Customer";
    public const string CustomerRole = "customer";
    public const string DisputeSubmissionRateLimit = "dispute-submission";
    public const string IdempotencyKeyHeader = "Idempotency-Key";
}
