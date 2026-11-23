using DisputePortal.Modules.DebitOrders.Application;
using DisputePortal.Modules.DebitOrders.Contracts;
using DisputePortal.Modules.DebitOrders.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.Modules.DebitOrders;

/// <summary>The module's only entry point for the host. Everything else in the assembly is internal.</summary>
public static class DebitOrdersModule
{
    public static IServiceCollection AddDebitOrdersModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DebitOrdersDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", DebitOrdersDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IDebitOrderStore, EfDebitOrderStore>();
        services.AddScoped<DebitOrderService>();
        services.AddScoped<IDebitOrderDirectory, DebitOrderDirectory>();
        services.AddScoped<DebitOrdersSeeder>();
        services.AddScoped<IValidator<DebitOrderListQuery>, DebitOrderListQueryValidator>();
        services.AddScoped<IValidator<MandateListQuery>, MandateListQueryValidator>();
        return services;
    }

    public static IHealthChecksBuilder AddDebitOrdersHealthChecks(this IHealthChecksBuilder builder) =>
        builder.AddDbContextCheck<DebitOrdersDbContext>("debit-orders-db", tags: ["ready"]);

    public static async Task MigrateDebitOrdersAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DebitOrdersDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public static async Task SeedDebitOrdersAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DebitOrdersSeeder>().SeedAsync(cancellationToken);
    }
}
