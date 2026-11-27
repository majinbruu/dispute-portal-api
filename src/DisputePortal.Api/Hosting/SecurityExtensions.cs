using System.Globalization;
using System.Threading.RateLimiting;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.Api.Hosting;

internal static class SecurityExtensions
{
    /// <summary>
    /// Validates Keycloak access tokens. The issuer the token carries is the public URL the browser used,
    /// while the API may reach Keycloak at an internal address, so the metadata address is configured separately.
    /// </summary>
    public static IServiceCollection AddPortalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection("Auth");
        var authority = auth["Authority"];

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = auth["Audience"];
            options.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", true);
            options.MapInboundClaims = false;

            if (auth["MetadataAddress"] is { Length: > 0 } metadataAddress)
            {
                options.MetadataAddress = metadataAddress;
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "preferred_username",
                RoleClaimType = "roles",
            };
        });

        // Two separate audiences. A customer token is refused on the agent endpoints and an agent token on the
        // customer endpoints, each with a 403, because each policy requires its own realm role.
        services.AddAuthorizationBuilder()
            .AddPolicy(ApiConventions.CustomerPolicy, policy => RequireRoleAndSubject(policy, ApiConventions.CustomerRole))
            .AddPolicy(ApiConventions.AgentPolicy, policy => RequireRoleAndSubject(policy, ApiConventions.AgentRole));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentCustomer, HttpCurrentCustomer>();
        services.AddScoped<ICurrentAgent, HttpCurrentAgent>();
        return services;
    }

    private static void RequireRoleAndSubject(AuthorizationPolicyBuilder policy, string role) => policy
        .RequireAuthenticatedUser()
        .RequireRole(role)
        .RequireAssertion(context => TokenSubject.TryRead(context.User, out _));

    /// <summary>
    /// A general limit per caller, and a tight limit on raising disputes. Limits are held in memory per instance;
    /// with several replicas the effective limit multiplies, so production would enforce this at the gateway.
    /// </summary>
    public static IServiceCollection AddPortalRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var requestsPerMinute = configuration.GetValue("RateLimiting:RequestsPerMinute", 300);
        var disputesPerMinute = configuration.GetValue("RateLimiting:DisputeSubmissionsPerMinute", 5);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsInfrastructurePath(context.Request.Path)
                    ? RateLimitPartition.GetNoLimiter("infrastructure")
                    : RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = requestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(ApiConventions.DisputeSubmissionRateLimit, context =>
                RateLimitPartition.GetTokenBucketLimiter(PartitionKey(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = disputesPerMinute,
                    TokensPerPeriod = disputesPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "You have made too many requests. Wait a moment and try again.",
                };
                problem.Extensions["code"] = "rate_limited";

                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .TryWriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problem });
            };
        });

        return services;
    }

    private static bool IsInfrastructurePath(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics");

    /// <summary>Limits follow the signed-in customer or agent, so users behind one address do not share a budget.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(TokenSubject.Claim)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
}
