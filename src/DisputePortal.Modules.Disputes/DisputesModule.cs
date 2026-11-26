using DisputePortal.Modules.DebitOrders.Contracts;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.Modules.Disputes;

/// <summary>The module's only entry point for the host. Everything else in the assembly is internal.</summary>
public static class DisputesModule
{
    public static IServiceCollection AddDisputesModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddDbContext<DisputesDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", DisputesDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.Configure<DisputeProcessingOptions>(configuration.GetSection(DisputeProcessingOptions.Section));
        services.Configure<SchemeSimulatorOptions>(configuration.GetSection(SchemeSimulatorOptions.Section));

        services.AddScoped<IDisputeRepository, EfDisputeRepository>();
        services.AddScoped<IDisputeStatusProvider, DisputeStatusProvider>();
        services.AddScoped<SubmitDisputeHandler>();
        services.AddScoped<DisputeQueries>();
        services.AddScoped<DisputeProcessingService>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<DisputesSeeder>();
        services.AddSingleton<ISchemeGateway, SimulatedSchemeGateway>();
        services.AddScoped<IValidator<SubmitDisputeRequest>, SubmitDisputeRequestValidator>();
        services.AddScoped<IValidator<DisputeListQuery>, DisputeListQueryValidator>();

        // The same image runs as an API pod (workers off) or a worker pod (workers on).
        if (configuration.GetSection(DisputeProcessingOptions.Section).GetValue("Enabled", true))
        {
            services.AddHostedService<DisputeProcessorWorker>();
            services.AddHostedService<OutboxDispatcherWorker>();
            services.AddHostedService<IdempotencyKeyCleanupWorker>();
        }

        return services;
    }

    public static IHealthChecksBuilder AddDisputesHealthChecks(this IHealthChecksBuilder builder) =>
        builder.AddDbContextCheck<DisputesDbContext>("disputes-db", tags: ["ready"]);

    public static async Task MigrateDisputesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DisputesDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public static async Task SeedDisputesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DisputesSeeder>().SeedAsync(cancellationToken);
    }
}
