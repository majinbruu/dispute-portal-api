namespace DisputePortal.Modules.Disputes.Contracts;

/// <summary>
/// Integration event published through the outbox whenever a dispute changes status.
/// It carries everything a consumer needs, so consumers never read this module's tables.
/// </summary>
public sealed record DisputeStatusChanged(
    Guid DisputeId,
    Guid CustomerId,
    string Reference,
    string Status,
    decimal Amount,
    string Currency,
    string CreditorName,
    string? OutcomeNote,
    DateTimeOffset OccurredAt);

/// <summary>
/// Implemented by modules that react to dispute status changes. Delivery is at least once,
/// so implementations must be idempotent; <paramref name="messageId"/> identifies the delivery.
/// </summary>
public interface IDisputeStatusChangedHandler
{
    Task HandleAsync(Guid messageId, DisputeStatusChanged message, CancellationToken cancellationToken);
}
