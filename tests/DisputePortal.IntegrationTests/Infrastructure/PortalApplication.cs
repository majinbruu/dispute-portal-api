using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>
/// The real application, started in-process from its own Program against a PostgreSQL database.
/// Three things differ from a deployment: tokens come from <see cref="TestIdentityProvider"/>, the clock is a
/// <see cref="TestClock"/>, and the background workers are not started, so a test runs each worker cycle itself.
/// </summary>
internal sealed class PortalApplication(string connectionString, TestIdentityProvider identity, TestClock clock, bool migrateOnStartup)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so appsettings.Development.json (local database, demo seed) is not loaded.
        builder.UseEnvironment("IntegrationTests");

        // Program reads its configuration while it builds, so these are passed as host settings, which arrive first.
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Auth:Authority", TestIdentityProvider.Issuer);
        builder.UseSetting("Database:MigrateOnStartup", migrateOnStartup.ToString());
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseSetting("Workers:Enabled", "false");
        builder.UseSetting("Logging:LogLevel:Default", "None");

        // The limits are per caller and held in memory, so they would carry over from one test to the next.
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:DisputeSubmissionsPerMinute", "100000");
        builder.UseSetting("RateLimiting:CommentsPerMinute", "100000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);

            // The signing keys are fetched from the identity provider as in production. Only the transport is replaced.
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options => options.BackchannelHttpHandler = identity.CreateBackchannel());
        });
    }
}

/// <summary>The application's clock. A test moves it instead of waiting.</summary>
internal sealed class TestClock : TimeProvider
{
    /// <summary>Where every test starts: 10:00 in South Africa on 27 November 2026.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);

    private long _utcTicks = Start.UtcTicks;

    public DateTimeOffset Now => GetUtcNow();

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);

    public void Reset() => Interlocked.Exchange(ref _utcTicks, Start.UtcTicks);
}
