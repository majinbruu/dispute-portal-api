using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Disputes.Infrastructure;

/// <summary>
/// Seeds three demo disputes for the first demo customer: one already refunded, one that the worker will
/// refund shortly after start-up, and one that the simulated scheme will decline.
/// </summary>
internal sealed class DisputesSeeder(DisputesDbContext db, ITransactionDirectory transactions, TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Disputes.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var refunded = await SubmitAsync(DemoData.RefundedDebitOrderId, "not_authorised", now.AddDays(-9), cancellationToken);
        if (refunded is not null)
        {
            var sentAt = now.AddDays(-9).AddMinutes(2);
            refunded.MarkSentToScheme(ReturnMessageFactory.Create(refunded, sentAt), DisputeActor.System, sentAt);
            refunded.MarkRefunded(sentAt.AddMinutes(3));
        }

        await SubmitAsync(DemoData.PendingDebitOrderId, "incorrect_amount", now, cancellationToken);
        await SubmitAsync(DemoData.DeclinedDebitOrderId, "cancelled", now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dispute?> SubmitAsync(Guid transactionId, string reasonId, DateTimeOffset submittedAt, CancellationToken cancellationToken)
    {
        var transaction = await transactions.FindAsync(transactionId, DemoData.Thandi, cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        var dispute = Dispute.Submit(
            Guid.NewGuid(),
            DisputeReference.New(submittedAt),
            DemoData.Thandi,
            DisputeMapper.ToDisputedTransaction(transaction),
            reasonId,
            comment: null,
            submittedAt);

        db.Disputes.Add(dispute);
        return dispute;
    }
}
