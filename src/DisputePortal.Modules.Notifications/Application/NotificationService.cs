using System.Globalization;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Notifications.Domain;
using DisputePortal.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Notifications.Application;

internal sealed record NotificationDto(
    Guid Id,
    NotificationCategory Category,
    string Title,
    string Body,
    Guid? RelatedDisputeId,
    DateTimeOffset CreatedAt,
    bool IsRead);

internal sealed record UnreadCountDto(int Unread);

/// <summary>Query string for the inbox.</summary>
internal sealed class NotificationListQuery
{
    [FromQuery(Name = "unreadOnly")]
    public bool? UnreadOnly { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

internal sealed class NotificationListQueryValidator : AbstractValidator<NotificationListQuery>
{
    public NotificationListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(q => q.PageSize.HasValue);
    }
}

internal interface INotificationStore
{
    Task<bool> ExistsForSourceAsync(Guid sourceMessageId, CancellationToken cancellationToken);

    /// <summary>Returns false if a notification for the same source message already exists.</summary>
    Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken);

    Task<PagedResult<Notification>> ListAsync(Guid customerId, bool unreadOnly, PageRequest page, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid customerId, CancellationToken cancellationToken);

    Task<Notification?> FindForUpdateAsync(Guid notificationId, Guid customerId, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Inbox use cases.</summary>
internal sealed class NotificationService(INotificationStore store, TimeProvider timeProvider)
{
    public static readonly Error NotFound = Error.NotFound("notification_not_found", "The notification was not found.");

    public async Task<PagedResult<NotificationDto>> ListAsync(Guid customerId, NotificationListQuery query, CancellationToken cancellationToken)
    {
        var page = await store.ListAsync(customerId, query.UnreadOnly ?? false, new PageRequest(query.Page, query.PageSize), cancellationToken);
        return page.Map(ToDto);
    }

    public async Task<UnreadCountDto> CountUnreadAsync(Guid customerId, CancellationToken cancellationToken) =>
        new(await store.CountUnreadAsync(customerId, cancellationToken));

    public async Task<Result<NotificationDto>> MarkReadAsync(Guid customerId, Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await store.FindForUpdateAsync(notificationId, customerId, cancellationToken);
        if (notification is null)
        {
            return NotFound;
        }

        notification.MarkRead(timeProvider.GetUtcNow());
        await store.SaveAsync(cancellationToken);
        return Result<NotificationDto>.Success(ToDto(notification));
    }

    private static NotificationDto ToDto(Notification notification) => new(
        notification.Id,
        notification.Category,
        notification.Title,
        notification.Body,
        notification.RelatedDisputeId,
        notification.CreatedAt,
        notification.IsRead);
}

/// <summary>
/// Consumes dispute status changes from the outbox and writes the simulated notification.
/// Delivery is at least once, so a repeated message must not create a second notification.
/// </summary>
internal sealed class DisputeStatusChangedHandler(INotificationStore store) : IDisputeStatusChangedHandler
{
    public async Task HandleAsync(Guid messageId, DisputeStatusChanged message, CancellationToken cancellationToken)
    {
        if (await store.ExistsForSourceAsync(messageId, cancellationToken))
        {
            return;
        }

        var notification = NotificationFactory.Create(messageId, message);
        if (notification is not null)
        {
            // TryAdd also relies on the unique index, which covers two workers handling the same message at once.
            await store.TryAddAsync(notification, cancellationToken);
        }
    }
}

/// <summary>
/// Consumes an agent's comment from the outbox and tells the customer in their inbox. The message ID is the
/// notification's source, as for status changes, so a repeated delivery does not create a second notification.
/// </summary>
internal sealed class AgentCommentAddedHandler(INotificationStore store) : IAgentCommentAddedHandler
{
    public async Task HandleAsync(Guid messageId, AgentCommentAdded message, CancellationToken cancellationToken)
    {
        if (await store.ExistsForSourceAsync(messageId, cancellationToken))
        {
            return;
        }

        await store.TryAddAsync(NotificationFactory.Create(messageId, message), cancellationToken);
    }
}

internal static class NotificationFactory
{
    /// <summary>The comment itself is not copied into the inbox. The customer reads it, and can reply, on the dispute.</summary>
    public static Notification Create(Guid messageId, AgentCommentAdded message) => new(
        message.CustomerId,
        messageId,
        NotificationCategory.DisputeComment,
        "New comment on your dispute",
        $"An agent added a comment to dispute {message.Reference}. Open the dispute to read it and reply.",
        message.DisputeId,
        message.OccurredAt);

    /// <summary>Returns null for a status that has no customer message.</summary>
    public static Notification? Create(Guid messageId, DisputeStatusChanged message)
    {
        var amount = FormatAmount(message.Amount, message.Currency);

        // A debit order is collected by a creditor. A card purchase is made at a merchant.
        var where = message.TransactionType == DisputedTransactionTypes.Card
            ? $"at {message.CounterpartyName}"
            : $"collected by {message.CounterpartyName}";

        return message.Status switch
        {
            "Submitted" => Build(
                NotificationCategory.DisputeReceived,
                "We received your dispute",
                $"Your dispute {message.Reference} for {amount} {where} has been received."),
            "SentToScheme" => Build(
                NotificationCategory.DisputeInProgress,
                "Your dispute is in progress",
                $"Dispute {message.Reference} was sent for processing. We will let you know the outcome."),
            "Refunded" => Build(
                NotificationCategory.DisputeRefunded,
                "Your dispute was successful",
                $"Dispute {message.Reference} is successful. Your account has been credited with {amount}."),
            "Declined" => Build(
                NotificationCategory.DisputeDeclined,
                "Your dispute was declined",
                $"Dispute {message.Reference} for {amount} was declined. {message.OutcomeNote}".TrimEnd()),
            "Withdrawn" => Build(
                NotificationCategory.DisputeWithdrawn,
                "You withdrew your dispute",
                $"Dispute {message.Reference} for {amount} {where} was withdrawn. Nothing was sent for processing."),
            _ => null,
        };

        Notification Build(NotificationCategory category, string title, string body) =>
            new(message.CustomerId, messageId, category, title, body, message.DisputeId, message.OccurredAt);
    }

    /// <summary>Rand amounts use the familiar "R" prefix. Any other currency is shown with its ISO 4217 code.</summary>
    private static string FormatAmount(decimal amount, string currency) =>
        string.Equals(currency, "ZAR", StringComparison.Ordinal)
            ? string.Create(CultureInfo.InvariantCulture, $"R{amount:N2}")
            : string.Create(CultureInfo.InvariantCulture, $"{currency} {amount:N2}");
}
