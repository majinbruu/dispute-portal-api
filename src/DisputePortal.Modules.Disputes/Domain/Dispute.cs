using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Domain;

internal enum DisputeStatus
{
    /// <summary>Recorded and waiting for an agent to review it. Not yet sent to the scheme.</summary>
    Submitted,

    /// <summary>The pacs.004 return or the chargeback has been generated and sent.</summary>
    SentToScheme,

    /// <summary>Terminal: the customer was refunded.</summary>
    Refunded,

    /// <summary>Terminal: an agent rejected the dispute, or the scheme did.</summary>
    Declined,

    /// <summary>Terminal: the customer withdrew the dispute before an agent reviewed it. Nothing was sent.</summary>
    Withdrawn,
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

/// <param name="MandateReference">Null for an EFT debit order, which has no mandate.</param>
internal sealed record DisputedDebitOrder(string EndToEndId, string MandateType, string? MandateReference);

internal sealed record DisputedCard(string Scheme, string EntryMode, string Verification, DateOnly? PostingDate);

internal sealed class DisputeEvent
{
    private DisputeEvent()
    {
    }

    internal DisputeEvent(Guid disputeId, DisputeStatus status, string description, DisputeActor actor, DateTimeOffset occurredAt)
    {
        if (actor.Type != DisputeActorType.System && actor.SubjectId is null)
        {
            throw new DisputeRuleException($"An event caused by a {actor.Type} must record who that was.");
        }

        Id = Guid.NewGuid();
        DisputeId = disputeId;
        Status = status;
        Description = description;
        ActorType = actor.Type;
        ActorSubjectId = actor.SubjectId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid DisputeId { get; private set; }

    public DisputeStatus Status { get; private set; }

    public string Description { get; private set; } = null!;

    public DisputeActorType ActorType { get; private set; }

    /// <summary>The token subject of the customer or agent who caused the event. Null when the system did.</summary>
    public Guid? ActorSubjectId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>One message in a dispute's comment thread, written by the customer or by an agent.</summary>
internal sealed class DisputeComment
{
    public const int MaxLength = 1000;

    private DisputeComment()
    {
    }

    internal DisputeComment(Guid disputeId, DisputeActor author, string body, DateTimeOffset createdAt)
    {
        if (author.Type == DisputeActorType.System || author.SubjectId is null)
        {
            throw new DisputeRuleException("A comment is written by a customer or an agent.");
        }

        var text = body?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxLength)
        {
            throw new DisputeRuleException($"A comment must be between 1 and {MaxLength} characters.");
        }

        Id = Guid.NewGuid();
        DisputeId = disputeId;
        AuthorType = author.Type;
        AuthorSubjectId = author.SubjectId.Value;
        Body = text;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid DisputeId { get; private set; }

    public DisputeActorType AuthorType { get; private set; }

    /// <summary>Kept for the audit trail. The customer is only ever shown the author type.</summary>
    public Guid AuthorSubjectId { get; private set; }

    public string Body { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Something that happened to a dispute and that other modules are told about through the outbox.</summary>
internal interface IDisputeDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Raised on every status change. Written to the outbox in the same transaction as the change.</summary>
internal sealed record DisputeStatusChangedDomainEvent(DisputeStatus Status, DateTimeOffset OccurredAt) : IDisputeDomainEvent;

/// <summary>Raised when an agent adds a comment, so the customer can be told. A customer's own comment raises nothing.</summary>
internal sealed record AgentCommentAddedDomainEvent(Guid CommentId, DateTimeOffset OccurredAt) : IDisputeDomainEvent;

/// <summary>
/// A customer's dispute of one transaction. The aggregate owns the lifecycle:
/// Submitted -> SentToScheme -> Refunded or Declined, where an agent's approval (or the automatic approval)
/// sends a submitted dispute on. From Submitted an agent can also reject it (Declined) and the customer can
/// withdraw it (Withdrawn). Every transition appends an event with who caused it, which gives the customer
/// a progress timeline and the bank an audit trail.
/// </summary>
internal sealed class Dispute
{
    public const int MaxRejectionReasonLength = 500;

    private readonly List<DisputeEvent> _events = [];
    private readonly List<DisputeComment> _comments = [];
    private readonly List<IDisputeDomainEvent> _domainEvents = [];

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

    /// <summary>DebiCheck and Registered Mandate debit orders only. An EFT debit order has no mandate.</summary>
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

    /// <summary>When the dispute reached a final status: Refunded, Declined or Withdrawn.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Why the dispute ended the way it did: the scheme's answer, or the agent's reason for rejecting it.</summary>
    public string? OutcomeNote { get; private set; }

    /// <summary>What was sent to the scheme, stored as JSON: a simulated pacs.004 payment return, or a simulated chargeback record.</summary>
    public string? SchemePayload { get; private set; }

    /// <summary>
    /// Optimistic concurrency token (PostgreSQL xmin). It changes with every status change, so when a withdrawal
    /// and an agent's decision race, the second one to save fails instead of overwriting the first.
    /// </summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<DisputeEvent> Events => _events;

    public IReadOnlyCollection<DisputeComment> Comments => _comments;

    public IReadOnlyCollection<IDisputeDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Waiting for an agent. Only now can it be approved, rejected or withdrawn.</summary>
    public bool IsAwaitingReview => Status == DisputeStatus.Submitted;

    /// <summary>Not yet final. Comments can be added while a dispute is open.</summary>
    public bool IsOpen => Status is DisputeStatus.Submitted or DisputeStatus.SentToScheme;

    public bool IsCompleted => !IsOpen;

    /// <param name="comment">Optional. When given it becomes the first comment in the thread.</param>
    public static Dispute Submit(
        Guid id,
        string reference,
        Guid customerId,
        DisputedTransaction transaction,
        string reasonId,
        string? comment,
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

        var customer = DisputeActor.Customer(customerId);
        dispute.Record(DisputeStatus.Submitted, "We received your dispute.", customer, now);
        if (comment is not null)
        {
            dispute.AddComment(customer, comment, now);
        }

        return dispute;
    }

    /// <summary>
    /// Sends the dispute for processing. This is what an agent's approval does, and what the automatic approval
    /// does when it is switched on; <paramref name="approvedBy"/> says which.
    /// </summary>
    public void MarkSentToScheme(string schemePayload, DisputeActor approvedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemePayload);
        if (approvedBy.Type == DisputeActorType.Customer)
        {
            throw new DisputeRuleException("A customer cannot approve a dispute.");
        }

        EnsureStatus(DisputeStatus.Submitted, DisputeStatus.SentToScheme);

        Status = DisputeStatus.SentToScheme;
        SentToSchemeAt = now;
        SchemePayload = schemePayload;
        Record(DisputeStatus.SentToScheme, "Your dispute was sent for processing.", approvedBy, now);
    }

    /// <summary>An agent turns the dispute down before anything is sent. The reason is shown to the customer.</summary>
    public void Reject(Guid agentId, string reason, DateTimeOffset now)
    {
        var note = reason?.Trim() ?? string.Empty;
        if (note.Length is 0 or > MaxRejectionReasonLength)
        {
            throw new DisputeRuleException($"A rejection reason must be between 1 and {MaxRejectionReasonLength} characters.");
        }

        EnsureStatus(DisputeStatus.Submitted, DisputeStatus.Declined);

        Status = DisputeStatus.Declined;
        CompletedAt = now;
        OutcomeNote = note;
        Record(DisputeStatus.Declined, note, DisputeActor.Agent(agentId), now);
    }

    /// <summary>The customer takes the dispute back. Only possible while it waits for an agent, so nothing was sent.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        EnsureStatus(DisputeStatus.Submitted, DisputeStatus.Withdrawn);

        Status = DisputeStatus.Withdrawn;
        CompletedAt = now;
        Record(DisputeStatus.Withdrawn, "You withdrew this dispute.", DisputeActor.Customer(CustomerId), now);
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        EnsureStatus(DisputeStatus.SentToScheme, DisputeStatus.Refunded);

        Status = DisputeStatus.Refunded;
        CompletedAt = now;
        OutcomeNote = "Your account has been credited.";
        Record(DisputeStatus.Refunded, OutcomeNote, DisputeActor.System, now);
    }

    /// <summary>The scheme's answer was no.</summary>
    public void MarkDeclined(string note, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        EnsureStatus(DisputeStatus.SentToScheme, DisputeStatus.Declined);

        Status = DisputeStatus.Declined;
        CompletedAt = now;
        OutcomeNote = note;
        Record(DisputeStatus.Declined, note, DisputeActor.System, now);
    }

    /// <summary>Adds to the comment thread. The customer and agents may comment while the dispute is open.</summary>
    public DisputeComment AddComment(DisputeActor author, string body, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DisputeRuleException($"A comment cannot be added to a dispute that is {Status}.");
        }

        if (author.Type == DisputeActorType.Customer && author.SubjectId != CustomerId)
        {
            throw new DisputeRuleException("Only the customer who raised a dispute can comment on it as the customer.");
        }

        var comment = new DisputeComment(Id, author, body, now);
        _comments.Add(comment);

        if (author.Type == DisputeActorType.Agent)
        {
            _domainEvents.Add(new AgentCommentAddedDomainEvent(comment.Id, now));
        }

        return comment;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void EnsureStatus(DisputeStatus required, DisputeStatus target)
    {
        if (Status != required)
        {
            throw new DisputeRuleException($"A dispute cannot move from {Status} to {target}.");
        }
    }

    private void Record(DisputeStatus status, string description, DisputeActor actor, DateTimeOffset now)
    {
        _events.Add(new DisputeEvent(Id, status, description, actor, now));
        _domainEvents.Add(new DisputeStatusChangedDomainEvent(status, now));
    }
}

/// <summary>A dispute invariant was broken. This signals a bug or a race, not an expected business outcome.</summary>
internal sealed class DisputeRuleException(string message) : InvalidOperationException(message);
