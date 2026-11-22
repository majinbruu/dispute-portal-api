using DisputePortal.SharedKernel;

namespace DisputePortal.Api.Hosting;

/// <summary>Reads the customer ID from the subject claim of the validated access token.</summary>
internal sealed class HttpCurrentCustomer(IHttpContextAccessor accessor) : ICurrentCustomer
{
    public const string SubjectClaim = "sub";

    public Guid CustomerId =>
        TryGetCustomerId(accessor.HttpContext?.User.FindFirst(SubjectClaim)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("No authenticated customer. The endpoint must require the customer policy.");

    public static bool TryGetCustomerId(string? subject, out Guid customerId) => Guid.TryParse(subject, out customerId);
}
