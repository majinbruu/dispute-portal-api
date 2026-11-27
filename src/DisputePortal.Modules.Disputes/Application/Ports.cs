using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>
/// Persistence port for disputes. What a customer reads or changes is always scoped to that customer.
/// The methods for agents say so in their names and reach across customers.
/// </summary>
internal interface IDisputeRepository
{
    Task<IdempotencyRecord?> FindIdempotencyRecordAsync(Guid customerId, string key, CancellationToken cancellationToken);

    Task<Dispute?> FindAsync(Guid disputeId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>Loads one of the customer's disputes in order to change it. Follow it with <see cref="TrySaveAsync"/>.</summary>
    Task<Dispute?> FindForChangeAsync(Guid disputeId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>Loads any customer's dispute for an agent to read.</summary>
    Task<Dispute?> FindForAgentAsync(Guid disputeId, CancellationToken cancellationToken);

    /// <summary>Loads any customer's dispute for an agent to change. Follow it with <see cref="TrySaveAsync"/>.</summary>
    Task<Dispute?> FindForChangeByAgentAsync(Guid disputeId, CancellationToken cancellationToken);

    Task<PagedResult<Dispute>> ListForAgentAsync(AgentDisputeFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the changes made to a loaded dispute, with its events, comments and outbox messages, in one transaction.
    /// Returns false when someone else changed the dispute's status after it was loaded. Nothing is written then.
    /// </summary>
    Task<bool> TrySaveAsync(CancellationToken cancellationToken);

    /// <summary>Whether the transaction already has a dispute. A withdrawn dispute does not count, so the transaction can be disputed again.</summary>
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
