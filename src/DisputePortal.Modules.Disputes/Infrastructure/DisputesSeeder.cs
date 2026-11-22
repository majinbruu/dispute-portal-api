using DisputePortal.Modules.DebitOrders.Contracts;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Disputes.Infrastructure;

/// <summary>
/// Seeds three demo disputes for the first demo customer: one already refunded, one that the worker will
/// refund shortly after start-up, and one that the simulated scheme will decline.
/// </summary>
internal sealed class DisputesSeeder(DisputesDbContext db, IDebitOrderDirectory debitOrders, TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Disputes.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var refunded = await SubmitAsync(DemoData.RefundedDebitOrderId, "MD01", null, now.AddDays(-9), cancellationToken);
        if (refunded is not null)
        {
            var sentAt = now.AddDays(-9).AddMinutes(2);
            refunded.MarkSentToScheme(ReturnMessageFactory.Create(refunded, sentAt), sentAt);
            refunded.MarkRefunded(sentAt.AddMinutes(3));
        }

        await SubmitAsync(DemoData.PendingDebitOrderId, "AM09", "The amount went up without notice.", now, cancellationToken);
        await SubmitAsync(DemoData.DeclinedDebitOrderId, "MD06", "I cancelled this membership.", now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dispute?> SubmitAsync(Guid debitOrderId, string reasonCode, string? comment, DateTimeOffset submittedAt, CancellationToken cancellationToken)
    {
        var debitOrder = await debitOrders.FindAsync(debitOrderId, DemoData.Thandi, cancellationToken);
        if (debitOrder is null)
        {
            return null;
        }

        var dispute = Dispute.Submit(
            Guid.NewGuid(),
            DisputeReference.New(submittedAt),
            DemoData.Thandi,
            new DisputedDebitOrder(
                debitOrder.Id,
                debitOrder.TransactionId,
                debitOrder.EndToEndId,
                new Money(debitOrder.Amount, debitOrder.Currency),
                debitOrder.ActionDate,
                debitOrder.MandateType,
                debitOrder.MandateReference,
                debitOrder.CreditorName,
                debitOrder.DebtorAccountMasked),
            DisputeReasons.Find(reasonCode)!,
            comment,
            submittedAt);

        db.Disputes.Add(dispute);
        return dispute;
    }
}
