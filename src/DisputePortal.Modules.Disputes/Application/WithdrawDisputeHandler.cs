using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>
/// Withdraws a customer's own dispute while it still waits for an agent. An agent may decide on the same dispute
/// at the same moment. Both load it as Submitted, and the row version lets only the first save through,
/// so exactly one of them wins and the other is told about the conflict.
/// </summary>
internal sealed class WithdrawDisputeHandler(IDisputeRepository disputes, TimeProvider timeProvider)
{
    public async Task<Result<Dispute>> HandleAsync(Guid customerId, Guid disputeId, CancellationToken cancellationToken)
    {
        // Scoped to the customer, so someone else's dispute looks exactly like one that does not exist.
        var dispute = await disputes.FindForChangeAsync(disputeId, customerId, cancellationToken);
        if (dispute is null)
        {
            return DisputeErrors.DisputeNotFound;
        }

        if (!dispute.IsAwaitingReview)
        {
            return DisputeErrors.NotWithdrawable;
        }

        dispute.Withdraw(timeProvider.GetUtcNow());

        if (!await disputes.TrySaveAsync(cancellationToken))
        {
            // An agent's decision was saved first.
            return DisputeErrors.NotWithdrawable;
        }

        PortalMetrics.DisputesWithdrawn.Inc();
        return Result<Dispute>.Success(dispute);
    }
}
