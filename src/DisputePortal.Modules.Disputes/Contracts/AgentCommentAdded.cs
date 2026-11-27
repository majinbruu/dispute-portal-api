namespace DisputePortal.Modules.Disputes.Contracts;

/// <summary>
/// Integration event published through the outbox when an agent adds a comment to a dispute.
/// It does not carry the comment text or the agent's identity: the customer reads the comment on the dispute.
/// </summary>
public sealed record AgentCommentAdded(
    Guid DisputeId,
    Guid CustomerId,
    string Reference,
    Guid CommentId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Implemented by modules that react to an agent's comment. Delivery is at least once,
/// so implementations must be idempotent; <paramref name="messageId"/> identifies the delivery.
/// </summary>
public interface IAgentCommentAddedHandler
{
    Task HandleAsync(Guid messageId, AgentCommentAdded message, CancellationToken cancellationToken);
}
