using System.Net.Http.Headers;
using DisputePortal.Modules.Disputes.Infrastructure;
using DisputePortal.Modules.Notifications.Infrastructure;
using DisputePortal.Modules.Transactions;
using DisputePortal.Modules.Transactions.Infrastructure;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyTrait("Category", "Integration")]

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>Every integration test class joins this collection, so they share one PostgreSQL container and run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class PortalCollection : ICollectionFixture<PortalFixture>
{
    public const string Name = "Portal";
}

/// <summary>
/// One PostgreSQL container and one running application for the whole test run. The application's database is
/// created by the application's own migrations. <see cref="ResetAsync"/> gives each test the same starting data.
/// </summary>
public sealed class PortalFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    internal TestIdentityProvider Identity { get; } = new();

    internal TestClock Clock { get; } = new();

    internal PortalApplication App { get; private set; } = null!;

    internal string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        App = StartApplication(ConnectionString, migrateOnStartup: true);
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await _postgres.DisposeAsync();
        Identity.Dispose();
    }

    /// <summary>Starts another instance of the application, for the tests that need a database of their own.</summary>
    internal PortalApplication StartApplication(string connectionString, bool migrateOnStartup)
    {
        var application = new PortalApplication(connectionString, Identity, Clock, migrateOnStartup);

        // Building the host runs Program up to the point where it starts listening, which includes the migrations.
        _ = application.Services;
        return application;
    }

    /// <summary>Creates an empty database in the shared container and returns its connection string.</summary>
    internal async Task<string> CreateDatabaseAsync(string name)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>
    /// The starting point of every test: the clock back at its start, every table empty, and the demo
    /// transactions and mandates recorded by the application's own seeder. There are no disputes.
    /// </summary>
    internal async Task ResetAsync()
    {
        Clock.Reset();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var truncate = new NpgsqlCommand(
            """
            DO $$
            DECLARE name text;
            BEGIN
                FOR name IN
                    SELECT format('%I.%I', schemaname, tablename) FROM pg_tables
                    WHERE schemaname IN ('transactions', 'disputes', 'notifications') AND tablename <> '__ef_migrations_history'
                LOOP
                    EXECUTE 'TRUNCATE TABLE ' || name || ' CASCADE';
                END LOOP;
            END $$;
            """,
            connection);
        await truncate.ExecuteNonQueryAsync();

        await App.Services.SeedTransactionsAsync();
    }

    internal HttpClient Thandi() => ClientFor(DemoData.Thandi, ApiConventions.CustomerRole);

    internal HttpClient Sipho() => ClientFor(DemoData.Sipho, ApiConventions.CustomerRole);

    internal HttpClient Agent() => ClientFor(DemoData.Zanele, ApiConventions.AgentRole);

    internal HttpClient ClientFor(Guid subject, params string[] roles) => ClientWithToken(Identity.TokenFor(subject, roles));

    /// <param name="application">The instance to call. The shared one unless a test started its own.</param>
    internal HttpClient ClientWithToken(string token, PortalApplication? application = null)
    {
        var client = (application ?? App).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    internal HttpClient Anonymous() => App.CreateClient();

    /// <summary>Reads or changes the disputes tables directly, in a scope of its own.</summary>
    internal Task<T> WithDisputesAsync<T>(Func<DisputesDbContext, Task<T>> work) => WithAsync(App, work);

    internal Task<T> WithNotificationsAsync<T>(Func<NotificationsDbContext, Task<T>> work) => WithAsync(App, work);

    internal Task<T> WithTransactionsAsync<T>(Func<TransactionsDbContext, Task<T>> work) => WithAsync(App, work);

    internal static async Task<T> WithAsync<TContext, T>(PortalApplication application, Func<TContext, Task<T>> work)
        where TContext : DbContext
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TContext>());
    }
}
