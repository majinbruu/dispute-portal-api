using System.Security.Claims;
using DisputePortal.SharedKernel;

namespace DisputePortal.Api.Hosting;

/// <summary>The subject claim of the validated access token. Keycloak puts the user's ID there, which is a GUID.</summary>
internal static class TokenSubject
{
    public const string Claim = "sub";

    public static bool TryRead(ClaimsPrincipal? user, out Guid subject) => Guid.TryParse(user?.FindFirst(Claim)?.Value, out subject);
}

/// <summary>Reads the customer ID from the subject claim of the validated access token.</summary>
internal sealed class HttpCurrentCustomer(IHttpContextAccessor accessor) : ICurrentCustomer
{
    public Guid CustomerId =>
        TokenSubject.TryRead(accessor.HttpContext?.User, out var id)
            ? id
            : throw new InvalidOperationException("No authenticated customer. The endpoint must require the customer policy.");
}

/// <summary>Reads the agent ID from the subject claim of the validated access token.</summary>
internal sealed class HttpCurrentAgent(IHttpContextAccessor accessor) : ICurrentAgent
{
    public Guid AgentId =>
        TokenSubject.TryRead(accessor.HttpContext?.User, out var id)
            ? id
            : throw new InvalidOperationException("No authenticated agent. The endpoint must require the agent policy.");
}
