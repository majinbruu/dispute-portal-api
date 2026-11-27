using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.Modules.Transactions.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.Modules.Transactions;

/// <summary>The module's only entry point for the host. Everything else in the assembly is internal.</summary>
public static class TransactionsModule
{
    public static IServiceCollection AddTransactionsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TransactionsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TransactionsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IDebitOrderStore, EfDebitOrderStore>();
        services.AddScoped<DebitOrderService>();
        services.AddScoped<IDebitOrderDirectory, DebitOrderDirectory>();
        services.AddScoped<TransactionsSeeder>();
        services.AddScoped<IValidator<DebitOrderListQuery>, DebitOrderListQueryValidator>();
        services.AddScoped<IValidator<MandateListQuery>, MandateListQueryValidator>();
        return services;
    }

    public static IHealthChecksBuilder AddTransactionsHealthChecks(this IHealthChecksBuilder builder) =>
        builder.AddDbContextCheck<TransactionsDbContext>("transactions-db", tags: ["ready"]);

    public static async Task MigrateTransactionsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>().Database;

        // This module was called DebitOrders and kept its tables in the debit_orders schema. EF Core finds its
        // migration history by schema name, so on a database created before the rename the history table has to
        // move first. Otherwise EF would see no history and run every migration again. The tables themselves are
        // moved by a migration. On a new database, and on every later start, this statement does nothing.
        await database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF to_regclass('debit_orders.__ef_migrations_history') IS NOT NULL
                   AND to_regclass('transactions.__ef_migrations_history') IS NULL THEN
                    CREATE SCHEMA IF NOT EXISTS transactions;
                    ALTER TABLE debit_orders.__ef_migrations_history SET SCHEMA transactions;
                END IF;
            END $$;
            """,
            cancellationToken);

        await database.MigrateAsync(cancellationToken);
    }

    public static async Task SeedTransactionsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TransactionsSeeder>().SeedAsync(cancellationToken);
    }
}
