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
    public static IServiceCollection AddPortalAuthentication(this IServiceCollection services, AuthSettings auth)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = auth.Authority;
            options.Audience = auth.Audience;
            options.RequireHttpsMetadata = auth.RequireHttpsMetadata;
            options.MapInboundClaims = false;

            if (auth.MetadataAddress is not null)
            {
                options.MetadataAddress = auth.MetadataAddress;
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = auth.Authority,
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
        // Deny by default: anything that names no policy still needs a valid token, so an endpoint added without
        // an attribute is closed, not open. What is meant to be public says so where it is mapped.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
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
    /// A general limit per caller, and tighter limits on raising disputes and on adding comments, which are the
    /// two places a caller writes free text or creates work for someone else. Limits are held in memory per instance;
    /// with several replicas the effective limit multiplies, so production would enforce this at the gateway.
    /// </summary>
    public static IServiceCollection AddPortalRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var requestsPerMinute = configuration.GetValue("RateLimiting:RequestsPerMinute", 300);
        var disputesPerMinute = configuration.GetValue("RateLimiting:DisputeSubmissionsPerMinute", 5);
        var commentsPerMinute = configuration.GetValue("RateLimiting:CommentsPerMinute", 20);

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

            options.AddPolicy(ApiConventions.DisputeSubmissionRateLimit, context => PerMinute(context, disputesPerMinute));
            options.AddPolicy(ApiConventions.DisputeCommentRateLimit, context => PerMinute(context, commentsPerMinute));

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

    private static RateLimitPartition<string> PerMinute(HttpContext context, int permits) =>
        RateLimitPartition.GetTokenBucketLimiter(PartitionKey(context), _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = permits,
            TokensPerPeriod = permits,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static bool IsInfrastructurePath(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics");

    /// <summary>Limits follow the signed-in customer or agent, so users behind one address do not share a budget.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(TokenSubject.Claim)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
}
