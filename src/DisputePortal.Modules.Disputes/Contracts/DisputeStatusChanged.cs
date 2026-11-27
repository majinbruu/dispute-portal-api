namespace DisputePortal.Modules.Disputes.Contracts;

/// <summary>
/// Integration event published through the outbox whenever a dispute changes status.
/// It carries everything a consumer needs, so consumers never read this module's tables.
/// </summary>
/// <param name="TransactionType">One of <see cref="DisputedTransactionTypes"/>.</param>
/// <param name="CounterpartyName">The creditor of a debit order, or the merchant of a card transaction.</param>
public sealed record DisputeStatusChanged(
    Guid DisputeId,
    Guid CustomerId,
    string Reference,
    string Status,
    string TransactionType,
    decimal Amount,
    string Currency,
    string CounterpartyName,
    string? OutcomeNote,
    DateTimeOffset OccurredAt);

/// <summary>The values of <see cref="DisputeStatusChanged.TransactionType"/>.</summary>
public static class DisputedTransactionTypes
{
    public const string DebitOrder = "DebitOrder";
    public const string Card = "Card";
}

/// <summary>
/// Implemented by modules that react to dispute status changes. Delivery is at least once,
/// so implementations must be idempotent; <paramref name="messageId"/> identifies the delivery.
/// </summary>
public interface IDisputeStatusChangedHandler
{
    Task HandleAsync(Guid messageId, DisputeStatusChanged message, CancellationToken cancellationToken);
}
