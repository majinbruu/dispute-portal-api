using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>Persistence port for disputes. Reads that return customer data are always scoped to a customer.</summary>
internal interface IDisputeRepository
{
    Task<IdempotencyRecord?> FindIdempotencyRecordAsync(Guid customerId, string key, CancellationToken cancellationToken);

    Task<Dispute?> FindAsync(Guid disputeId, Guid customerId, CancellationToken cancellationToken);

    Task<bool> ExistsForTransactionAsync(Guid transactionId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the dispute, its events, its outbox messages and the idempotency record in one transaction.
    /// Returns false when a uniqueness rule was violated, meaning another request got there first.
    /// </summary>
    Task<bool> TryAddAsync(Dispute dispute, IdempotencyRecord idempotencyRecord, CancellationToken cancellationToken);

    Task<PagedResult<Dispute>> ListAsync(Guid customerId, DisputeState? state, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>The scheme's answer to a dispute: accepted, which refunds the customer, or declined with a note for the customer.</summary>
internal readonly record struct SchemeOutcome(bool Accepted, string? DeclineNote)
{
    public static SchemeOutcome AcceptedOutcome { get; } = new(true, null);

    public static SchemeOutcome Declined(string note) => new(false, note);
}

/// <summary>The boundary to the payment schemes. The only implementation is a simulator.</summary>
internal interface ISchemeGateway
{
    SchemeOutcome Submit(Dispute dispute);
}
