using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Domain;

internal enum DisputeStatus
{
    /// <summary>Recorded, not yet sent to the scheme.</summary>
    Submitted,

    /// <summary>The pacs.004 return has been generated and sent.</summary>
    SentToScheme,

    /// <summary>Terminal: the customer was refunded.</summary>
    Refunded,

    /// <summary>Terminal: the scheme rejected the return.</summary>
    Declined,
}

/// <summary>Details of the disputed debit order, copied at submission so a dispute stands on its own.</summary>
internal sealed record DisputedDebitOrder(
    Guid DebitOrderId,
    string TransactionId,
    string EndToEndId,
    Money Amount,
    DateOnly ActionDate,
    string MandateType,
    string MandateReference,
    string CreditorName,
    string DebtorAccountMasked);

internal sealed class DisputeEvent
{
    private DisputeEvent()
    {
    }

    internal DisputeEvent(Guid disputeId, DisputeStatus status, string description, DateTimeOffset occurredAt)
    {
        Id = Guid.NewGuid();
        DisputeId = disputeId;
        Status = status;
        Description = description;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid DisputeId { get; private set; }

    public DisputeStatus Status { get; private set; }

    public string Description { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>Raised on every status change. Written to the outbox in the same transaction as the change.</summary>
internal sealed record DisputeStatusChangedDomainEvent(Dispute Dispute, DisputeStatus Status, DateTimeOffset OccurredAt);

/// <summary>
/// A customer's dispute of one debit order. The aggregate owns the lifecycle:
/// Submitted -> SentToScheme -> Refunded or Declined. Every transition appends an event,
/// which gives the customer a progress timeline and the bank an audit trail.
/// </summary>
internal sealed class Dispute
{
    private readonly List<DisputeEvent> _events = [];
    private readonly List<DisputeStatusChangedDomainEvent> _domainEvents = [];

    private Dispute()
    {
    }

    public Guid Id { get; private set; }

    public string Reference { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public Guid DebitOrderId { get; private set; }

    public string OriginalTransactionId { get; private set; } = null!;

    public string OriginalEndToEndId { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    public DateOnly ActionDate { get; private set; }

    public string MandateType { get; private set; } = null!;

    public string MandateReference { get; private set; } = null!;

    public string CreditorName { get; private set; } = null!;

    public string DebtorAccountMasked { get; private set; } = null!;

    public string ReasonCode { get; private set; } = null!;

    public string? Comment { get; private set; }

    public DisputeStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? SentToSchemeAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? OutcomeNote { get; private set; }

    /// <summary>The simulated pacs.004 payment return, stored as JSON.</summary>
    public string? ReturnPayload { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<DisputeEvent> Events => _events;

    public IReadOnlyCollection<DisputeStatusChangedDomainEvent> DomainEvents => _domainEvents;

    public bool IsCompleted => Status is DisputeStatus.Refunded or DisputeStatus.Declined;

    public static Dispute Submit(
        Guid id,
        string reference,
        Guid customerId,
        DisputedDebitOrder debitOrder,
        DisputeReason reason,
        string? comment,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        if (!reason.IsAllowedFor(debitOrder.MandateType))
        {
            throw new DisputeRuleException($"Reason {reason.Code} is not allowed for a {debitOrder.MandateType} debit order.");
        }

        var dispute = new Dispute
        {
            Id = id,
            Reference = reference,
            CustomerId = customerId,
            DebitOrderId = debitOrder.DebitOrderId,
            OriginalTransactionId = debitOrder.TransactionId,
            OriginalEndToEndId = debitOrder.EndToEndId,
            Amount = debitOrder.Amount.Amount,   // a dispute is always for the full collection amount
            Currency = debitOrder.Amount.Currency,
            ActionDate = debitOrder.ActionDate,
            MandateType = debitOrder.MandateType,
            MandateReference = debitOrder.MandateReference,
            CreditorName = debitOrder.CreditorName,
            DebtorAccountMasked = debitOrder.DebtorAccountMasked,
            ReasonCode = reason.Code,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            Status = DisputeStatus.Submitted,
            SubmittedAt = now,
        };

        dispute.Record(DisputeStatus.Submitted, "We received your dispute.", now);
        return dispute;
    }

    public void MarkSentToScheme(string returnPayload, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnPayload);
        EnsureStatus(DisputeStatus.Submitted, DisputeStatus.SentToScheme);

        Status = DisputeStatus.SentToScheme;
        SentToSchemeAt = now;
        ReturnPayload = returnPayload;
        Record(DisputeStatus.SentToScheme, "Your dispute was sent to the service provider's bank.", now);
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        EnsureStatus(DisputeStatus.SentToScheme, DisputeStatus.Refunded);

        Status = DisputeStatus.Refunded;
        CompletedAt = now;
        OutcomeNote = "This dispute is successful and a refund has been made to your account.";
        Record(DisputeStatus.Refunded, OutcomeNote, now);
    }

    public void MarkDeclined(string note, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        EnsureStatus(DisputeStatus.SentToScheme, DisputeStatus.Declined);

        Status = DisputeStatus.Declined;
        CompletedAt = now;
        OutcomeNote = note;
        Record(DisputeStatus.Declined, note, now);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void EnsureStatus(DisputeStatus required, DisputeStatus target)
    {
        if (Status != required)
        {
            throw new DisputeRuleException($"A dispute cannot move from {Status} to {target}.");
        }
    }

    private void Record(DisputeStatus status, string description, DateTimeOffset now)
    {
        _events.Add(new DisputeEvent(Id, status, description, now));
        _domainEvents.Add(new DisputeStatusChangedDomainEvent(this, status, now));
    }
}

/// <summary>A dispute invariant was broken. This signals a bug or a race, not an expected business outcome.</summary>
internal sealed class DisputeRuleException(string message) : InvalidOperationException(message);
