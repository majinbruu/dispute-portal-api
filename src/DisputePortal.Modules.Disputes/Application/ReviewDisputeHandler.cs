using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>
/// An agent's decision on a submitted dispute. Approval sends it for processing; rejection declines it with the
/// agent's reason. Two agents, or an agent and the withdrawing customer, may act on the same dispute at once:
/// the row version lets only the first save through, and the other is told about the conflict.
/// </summary>
internal sealed class ReviewDisputeHandler(IDisputeRepository disputes, DisputeProcessingService processing, TimeProvider timeProvider)
{
    public async Task<Result<Dispute>> ApproveAsync(Guid agentId, Guid disputeId, CancellationToken cancellationToken)
    {
        var result = await DecideAsync(disputeId, dispute => processing.SendToScheme(dispute, DisputeActor.Agent(agentId)), cancellationToken);
        if (result.IsSuccess)
        {
            ReviewMetrics.Approved(DisputeActorType.Agent);
        }

        return result;
    }

    public async Task<Result<Dispute>> RejectAsync(Guid agentId, Guid disputeId, string reason, CancellationToken cancellationToken)
    {
        var result = await DecideAsync(disputeId, dispute => dispute.Reject(agentId, reason, timeProvider.GetUtcNow()), cancellationToken);
        if (result.IsSuccess)
        {
            ReviewMetrics.Rejected();
        }

        return result;
    }

    private async Task<Result<Dispute>> DecideAsync(Guid disputeId, Action<Dispute> decide, CancellationToken cancellationToken)
    {
        var dispute = await disputes.FindForChangeByAgentAsync(disputeId, cancellationToken);
        if (dispute is null)
        {
            return DisputeErrors.DisputeNotFound;
        }

        if (!dispute.IsAwaitingReview)
        {
            return DisputeErrors.NotAwaitingReview;
        }

        decide(dispute);

        // False means the customer's withdrawal or another agent's decision was saved first.
        return await disputes.TrySaveAsync(cancellationToken)
            ? Result<Dispute>.Success(dispute)
            : DisputeErrors.NotAwaitingReview;
    }
}

/// <summary>Counts review decisions the same way wherever they are made: by an agent here, or by the worker's automatic approval.</summary>
internal static class ReviewMetrics
{
    public static void Approved(DisputeActorType by) => PortalMetrics.DisputesReviewed.Add(
        1,
        new KeyValuePair<string, object?>("decision", "Approved"),
        new KeyValuePair<string, object?>("by", by.ToString()));

    public static void Rejected()
    {
        PortalMetrics.DisputesReviewed.Add(
            1,
            new KeyValuePair<string, object?>("decision", "Rejected"),
            new KeyValuePair<string, object?>("by", nameof(DisputeActorType.Agent)));
        PortalMetrics.DisputesCompleted.Add(1, new KeyValuePair<string, object?>("outcome", nameof(DisputeStatus.Declined)));
    }
}
