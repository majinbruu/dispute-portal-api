using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>Persistence port for disputes. Reads that return customer data are always scoped to a customer.</summary>
internal interface IDisputeRepository
{
    Task<IdempotencyRecord?> FindIdempotencyRecordAsync(Guid customerId, string key, CancellationToken cancellationToken);

    Task<Dispute?> FindAsync(Guid disputeId, Guid customerId, CancellationToken cancellationToken);

    Task<bool> ExistsForDebitOrderAsync(Guid debitOrderId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the dispute, its events, its outbox messages and the idempotency record in one transaction.
    /// Returns false when a uniqueness rule was violated, meaning another request got there first.
    /// </summary>
    Task<bool> TryAddAsync(Dispute dispute, IdempotencyRecord idempotencyRecord, CancellationToken cancellationToken);

    Task<PagedResult<Dispute>> ListAsync(Guid customerId, DisputeState? state, PageRequest page, CancellationToken cancellationToken);
}

internal readonly record struct SchemeReturnResult(bool Accepted, string? RejectionReason)
{
    public static SchemeReturnResult AcceptedResult { get; } = new(true, null);

    public static SchemeReturnResult Rejected(string reason) => new(false, reason);
}

/// <summary>The boundary to the payment scheme. The only implementation is a simulator.</summary>
internal interface ISchemeGateway
{
    SchemeReturnResult SubmitReturn(Dispute dispute);
}
