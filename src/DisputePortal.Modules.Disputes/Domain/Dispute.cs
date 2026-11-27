using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Domain;

internal enum DisputeStatus
{
    /// <summary>Recorded, not yet sent to the scheme.</summary>
    Submitted,

    /// <summary>The pacs.004 return or the chargeback has been generated and sent.</summary>
    SentToScheme,

    /// <summary>Terminal: the customer was refunded.</summary>
    Refunded,

    /// <summary>Terminal: the scheme rejected the dispute.</summary>
    Declined,
}

internal enum DisputedTransactionType
{
    DebitOrder,
    Card,
}

/// <summary>
/// Details of the disputed transaction, copied at submission so a dispute stands on its own.
/// Exactly one of <see cref="DebitOrder"/> and <see cref="Card"/> is set, matching <see cref="Type"/>.
/// </summary>
/// <param name="SchemeReference">How the payment scheme identifies the transaction: the pacs.003 TxId of a debit order, or the acquirer reference number of a card transaction.</param>
/// <param name="TransactionDate">The action date of a debit order, or the day the card was used.</param>
internal sealed record DisputedTransaction(
    Guid TransactionId,
    DisputedTransactionType Type,
    string SchemeReference,
    Money Amount,
    DateOnly TransactionDate,
    string CounterpartyName,
    string AccountMasked,
    DisputedDebitOrder? DebitOrder = null,
    DisputedCard? Card = null);

internal sealed record DisputedDebitOrder(string EndToEndId, string MandateType, string MandateReference);

internal sealed record DisputedCard(string Scheme, string EntryMode, string Verification, DateOnly? PostingDate);

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
/// A customer's dispute of one transaction. The aggregate owns the lifecycle:
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

    public Guid TransactionId { get; private set; }

    public DisputedTransactionType TransactionType { get; private set; }

    /// <summary>The pacs.003 TxId of a debit order, or the acquirer reference number of a card transaction.</summary>
    public string SchemeReference { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>The action date of a debit order, or the day the card was used.</summary>
    public DateOnly TransactionDate { get; private set; }

    public string CounterpartyName { get; private set; } = null!;

    public string AccountMasked { get; private set; } = null!;

    /// <summary>Debit orders only: the pacs.003 EndToEndId.</summary>
    public string? OriginalEndToEndId { get; private set; }

    /// <summary>Debit orders only.</summary>
    public string? MandateType { get; private set; }

    /// <summary>Debit orders only.</summary>
    public string? MandateReference { get; private set; }

    /// <summary>Card transactions only.</summary>
    public string? CardScheme { get; private set; }

    /// <summary>Card transactions only.</summary>
    public DateOnly? PostingDate { get; private set; }

    /// <summary>The stable id of the reason the customer chose.</summary>
    public string ReasonId { get; private set; } = null!;

    /// <summary>What the payment scheme calls that reason for this transaction, worked out at submission.</summary>
    public string ReasonCode { get; private set; } = null!;

    public DisputeStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? SentToSchemeAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? OutcomeNote { get; private set; }

    /// <summary>What was sent to the scheme, stored as JSON: a simulated pacs.004 payment return, or a simulated chargeback record.</summary>
    public string? SchemePayload { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<DisputeEvent> Events => _events;

    public IReadOnlyCollection<DisputeStatusChangedDomainEvent> DomainEvents => _domainEvents;

    public bool IsCompleted => Status is DisputeStatus.Refunded or DisputeStatus.Declined;

    public static Dispute Submit(
        Guid id,
        string reference,
        Guid customerId,
        DisputedTransaction transaction,
        string reasonId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var hasDetailsForItsType = transaction.Type == DisputedTransactionType.Card
            ? transaction is { Card: not null, DebitOrder: null }
            : transaction is { DebitOrder: not null, Card: null };
        if (!hasDetailsForItsType)
        {
            throw new DisputeRuleException($"A {transaction.Type} dispute must carry the details of a {transaction.Type} transaction and no other.");
        }

        var reason = DisputeReasons.Resolve(reasonId, transaction)
            ?? throw new DisputeRuleException($"Reason '{reasonId}' is not allowed for this {transaction.Type} transaction.");

        var dispute = new Dispute
        {
            Id = id,
            Reference = reference,
            CustomerId = customerId,
            TransactionId = transaction.TransactionId,
            TransactionType = transaction.Type,
            SchemeReference = transaction.SchemeReference,
            Amount = transaction.Amount.Amount,
            Currency = transaction.Amount.Currency,
            TransactionDate = transaction.TransactionDate,
            CounterpartyName = transaction.CounterpartyName,
            AccountMasked = transaction.AccountMasked,
            OriginalEndToEndId = transaction.DebitOrder?.EndToEndId,
            MandateType = transaction.DebitOrder?.MandateType,
            MandateReference = transaction.DebitOrder?.MandateReference,
            CardScheme = transaction.Card?.Scheme,
            PostingDate = transaction.Card?.PostingDate,
            ReasonId = reason.Id,
            ReasonCode = reason.Code,
            Status = DisputeStatus.Submitted,
            SubmittedAt = now,
        };

        dispute.Record(DisputeStatus.Submitted, "We received your dispute.", now);
        return dispute;
    }

    public void MarkSentToScheme(string schemePayload, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemePayload);
        EnsureStatus(DisputeStatus.Submitted, DisputeStatus.SentToScheme);

        Status = DisputeStatus.SentToScheme;
        SentToSchemeAt = now;
        SchemePayload = schemePayload;
        Record(DisputeStatus.SentToScheme, "Your dispute was sent for processing.", now);
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        EnsureStatus(DisputeStatus.SentToScheme, DisputeStatus.Refunded);

        Status = DisputeStatus.Refunded;
        CompletedAt = now;
        OutcomeNote = "Your account has been credited.";
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
