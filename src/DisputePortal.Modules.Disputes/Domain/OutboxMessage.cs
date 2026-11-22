namespace DisputePortal.Modules.Disputes.Domain;

/// <summary>
/// An integration event waiting to be delivered. It is inserted in the same transaction as the state change
/// that caused it, so an event is never lost and never published for a change that rolled back.
/// </summary>
internal sealed class OutboxMessage
{
    public const int MaxAttempts = 5;

    private OutboxMessage()
    {
    }

    public OutboxMessage(Guid id, string type, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public bool IsAbandoned => ProcessedAt is null && Attempts >= MaxAttempts;

    public void MarkProcessed(DateTimeOffset now)
    {
        Attempts++;
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>Records a failed delivery and schedules the next attempt with exponential backoff: 10s, 20s, 40s, 80s.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        NextAttemptAt = now.AddSeconds(5 * Math.Pow(2, Attempts));
    }
}

/// <summary>Remembers the result of a dispute submission so a retried request returns the same dispute.</summary>
internal sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(Guid customerId, string key, string requestHash, Guid disputeId, DateTimeOffset createdAt)
    {
        CustomerId = customerId;
        Key = key;
        RequestHash = requestHash;
        DisputeId = disputeId;
        CreatedAt = createdAt;
    }

    public Guid CustomerId { get; private set; }

    public string Key { get; private set; } = null!;

    public string RequestHash { get; private set; } = null!;

    public Guid DisputeId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
