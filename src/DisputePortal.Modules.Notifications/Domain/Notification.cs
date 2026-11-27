namespace DisputePortal.Modules.Notifications.Domain;

internal enum NotificationCategory
{
    DisputeReceived,
    DisputeInProgress,
    DisputeRefunded,
    DisputeDeclined,
    DisputeWithdrawn,
    DisputeComment,
}

/// <summary>A simulated message to the customer, shown in the in-app inbox.</summary>
internal sealed class Notification
{
    private Notification()
    {
    }

    public Notification(
        Guid customerId,
        Guid sourceMessageId,
        NotificationCategory category,
        string title,
        string body,
        Guid? relatedDisputeId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        Id = Guid.NewGuid();
        CustomerId = customerId;
        SourceMessageId = sourceMessageId;
        Category = category;
        Title = title;
        Body = body;
        RelatedDisputeId = relatedDisputeId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>The outbox message that caused this notification. Unique, which makes the consumer idempotent.</summary>
    public Guid SourceMessageId { get; private set; }

    public NotificationCategory Category { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public Guid? RelatedDisputeId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    /// <summary>Marks the notification as read. Reading twice keeps the first timestamp.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
