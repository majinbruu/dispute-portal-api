using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Notifications.Application;
using DisputePortal.Modules.Notifications.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.Modules.Notifications;

/// <summary>The module's only entry point for the host. Everything else in the assembly is internal.</summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<NotificationsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<INotificationStore, EfNotificationStore>();
        services.AddScoped<NotificationService>();
        services.AddScoped<IDisputeStatusChangedHandler, DisputeStatusChangedHandler>();
        services.AddScoped<IValidator<NotificationListQuery>, NotificationListQueryValidator>();
        return services;
    }

    public static IHealthChecksBuilder AddNotificationsHealthChecks(this IHealthChecksBuilder builder) =>
        builder.AddDbContextCheck<NotificationsDbContext>("notifications-db", tags: ["ready"]);

    public static async Task MigrateNotificationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
