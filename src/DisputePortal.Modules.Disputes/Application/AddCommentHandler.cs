using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>
/// Adds a comment to a dispute's thread, for the customer who raised it or for an agent. Comments are allowed
/// while the dispute is open. An agent's comment also raises the event that tells the customer through the outbox.
/// </summary>
internal sealed class AddCommentHandler(IDisputeRepository disputes, TimeProvider timeProvider)
{
    public async Task<Result<Dispute>> ByCustomerAsync(Guid customerId, Guid disputeId, string body, CancellationToken cancellationToken)
    {
        // Scoped to the customer, so someone else's dispute looks exactly like one that does not exist.
        var dispute = await disputes.FindForChangeAsync(disputeId, customerId, cancellationToken);
        return await AddAsync(dispute, DisputeActor.Customer(customerId), body, cancellationToken);
    }

    public async Task<Result<Dispute>> ByAgentAsync(Guid agentId, Guid disputeId, string body, CancellationToken cancellationToken)
    {
        var dispute = await disputes.FindForChangeByAgentAsync(disputeId, cancellationToken);
        return await AddAsync(dispute, DisputeActor.Agent(agentId), body, cancellationToken);
    }

    private async Task<Result<Dispute>> AddAsync(Dispute? dispute, DisputeActor author, string body, CancellationToken cancellationToken)
    {
        if (dispute is null)
        {
            return DisputeErrors.DisputeNotFound;
        }

        if (!dispute.IsOpen)
        {
            return DisputeErrors.Closed;
        }

        dispute.AddComment(author, body, timeProvider.GetUtcNow());

        // A comment adds a row and leaves the dispute row alone, so today nothing can conflict with this save.
        // Should that change, a conflict would mean the dispute moved on while the comment was being written.
        if (!await disputes.TrySaveAsync(cancellationToken))
        {
            return DisputeErrors.Closed;
        }

        PortalMetrics.DisputeCommentsAdded.Add(1, new KeyValuePair<string, object?>("author", author.Type.ToString()));
        return Result<Dispute>.Success(dispute);
    }
}
