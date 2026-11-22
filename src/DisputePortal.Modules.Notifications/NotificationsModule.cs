using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Notifications.Application;
using DisputePortal.Modules.Notifications.Infrastructure;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/api/v1/notifications")
            .WithTags("Notifications")
            .RequireAuthorization(ApiConventions.CustomerPolicy);

        notifications.MapGet("/", async ([AsParameters] NotificationListQuery query, NotificationService service, ICurrentCustomer customer, CancellationToken cancellationToken) =>
                Results.Ok(await service.ListAsync(customer.CustomerId, query, cancellationToken)))
            .AddEndpointFilter<ValidationFilter<NotificationListQuery>>()
            .WithName("ListNotifications")
            .WithSummary("Lists the customer's inbox, newest first.")
            .Produces<PagedResult<NotificationDto>>()
            .ProducesValidationProblem();

        notifications.MapGet("/unread-count", async (NotificationService service, ICurrentCustomer customer, CancellationToken cancellationToken) =>
                Results.Ok(await service.CountUnreadAsync(customer.CustomerId, cancellationToken)))
            .WithName("GetUnreadNotificationCount")
            .WithSummary("Returns how many notifications the customer has not read.")
            .Produces<UnreadCountDto>();

        notifications.MapPost("/{id:guid}/read", async (Guid id, NotificationService service, ICurrentCustomer customer, CancellationToken cancellationToken) =>
            {
                var result = await service.MarkReadAsync(customer.CustomerId, id, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .WithName("MarkNotificationRead")
            .WithSummary("Marks a notification as read. Safe to repeat.")
            .Produces<NotificationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static async Task MigrateNotificationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
