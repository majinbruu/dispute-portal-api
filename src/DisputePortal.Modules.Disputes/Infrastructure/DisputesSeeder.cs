using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Disputes.Infrastructure;

/// <summary>Seeds the demo disputes once, against the demo transactions the Transactions module seeded.</summary>
internal sealed class DisputesSeeder(DisputesDbContext db, ITransactionDirectory transactions, TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Disputes.AnyAsync(cancellationToken))
        {
            return;
        }

        var found = new Dictionary<Guid, TransactionForDispute>();
        foreach (var (transactionId, customerId) in DemoDisputes.Transactions)
        {
            if (await transactions.FindAsync(transactionId, customerId, cancellationToken) is { } transaction)
            {
                found[transactionId] = transaction;
            }
        }

        // Saved through the same path as real disputes, so every status change and agent comment
        // below also reaches the outbox and becomes an inbox notification.
        db.Disputes.AddRange(DemoDisputes.Build(found, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// The demo disputes. Between them they show every status for a debit order and for a card transaction,
/// a withdrawal, an agent's rejection and a comment thread. They are built with the same aggregate methods
/// the application uses, so each one has a truthful timeline. Two are left with the scheme: the worker
/// declines them shortly after start-up, because their transactions are on the simulator's decline list.
/// </summary>
internal static class DemoDisputes
{
    private static readonly DisputeActor Agent = DisputeActor.Agent(DemoData.Zanele);

    /// <summary>The transactions the demo disputes are raised against, with the customer each belongs to.</summary>
    public static IReadOnlyList<(Guid TransactionId, Guid CustomerId)> Transactions { get; } =
    [
        (DemoData.RefundedDebitOrderId, DemoData.Thandi),
        (DemoData.PendingDebitOrderId, DemoData.Thandi),
        (DemoData.DeclinedDebitOrderId, DemoData.Thandi),
        (DemoData.WithdrawnDebitOrderId, DemoData.Thandi),
        (DemoData.AgentRejectedDebitOrderId, DemoData.Thandi),
        (DemoData.PendingCardId, DemoData.Thandi),
        (DemoData.RefundedCardId, DemoData.Thandi),
        (DemoData.WithdrawnCardId, DemoData.Thandi),
        (DemoData.DeclinedCardId, DemoData.Thandi),
        (DemoData.SiphoPendingCardId, DemoData.Sipho),
    ];

    /// <summary>Builds a dispute for each transaction that was found. A transaction that is missing is skipped.</summary>
    public static IReadOnlyList<Dispute> Build(IReadOnlyDictionary<Guid, TransactionForDispute> transactions, DateTimeOffset now)
    {
        var disputes = new List<Dispute>();

        // Debit orders.
        if (Submit(DemoData.RefundedDebitOrderId, "not_authorised", now.AddDays(-9)) is { } refunded)
        {
            Approve(refunded, now.AddDays(-9).AddHours(2));
            refunded.MarkRefunded(now.AddDays(-9).AddHours(2).AddMinutes(3));
        }

        // Waits for an agent, with a conversation under way.
        if (Submit(DemoData.PendingDebitOrderId, "incorrect_amount", now.AddHours(-3), "My DStv package is R879 a month, not R929.") is { } pending)
        {
            pending.AddComment(Agent, "Thank you. Did you add or change a package in the last two months?", now.AddHours(-2));
            pending.AddComment(DisputeActor.Customer(pending.CustomerId), "No, nothing changed on my side.", now.AddHours(-1));
        }

        // Approved just now. The simulated scheme declines it.
        if (Submit(DemoData.DeclinedDebitOrderId, "cancelled", now.AddMinutes(-20)) is { } declinedByScheme)
        {
            Approve(declinedByScheme, now);
        }

        Submit(DemoData.WithdrawnDebitOrderId, "duplicate", now.AddDays(-3))?.Withdraw(now.AddDays(-3).AddMinutes(25));

        Submit(DemoData.AgentRejectedDebitOrderId, "incorrect_amount", now.AddDays(-5), "This is more than my usual rates bill.")?.Reject(
            DemoData.Zanele,
            "The municipal statement for that month shows R1,452.75 was due, which is the amount that was collected.",
            now.AddDays(-4));

        // Card transactions.
        Submit(DemoData.PendingCardId, "not_authorised", now.AddHours(-1), "I have never ordered from Takealot with this card.");

        if (Submit(DemoData.RefundedCardId, "cancelled_recurring", now.AddDays(-6)) is { } refundedCard)
        {
            Approve(refundedCard, now.AddDays(-6).AddHours(1));
            refundedCard.MarkRefunded(now.AddDays(-6).AddHours(1).AddMinutes(3));
        }

        Submit(DemoData.WithdrawnCardId, "duplicate", now.AddDays(-1))?.Withdraw(now.AddDays(-1).AddMinutes(10));

        // Approved just now. The simulated scheme declines it: the merchant proves the charge.
        if (Submit(DemoData.DeclinedCardId, "not_received", now.AddMinutes(-30)) is { } declinedCard)
        {
            Approve(declinedCard, now);
        }

        // A second customer, so the agents' queue spans customers.
        Submit(DemoData.SiphoPendingCardId, "not_received", now.AddHours(-5));

        return disputes;

        Dispute? Submit(Guid transactionId, string reasonId, DateTimeOffset submittedAt, string? comment = null)
        {
            if (!transactions.TryGetValue(transactionId, out var transaction))
            {
                return null;
            }

            var dispute = Dispute.Submit(
                Guid.NewGuid(),
                DisputeReference.New(submittedAt),
                transaction.CustomerId,
                DisputeMapper.ToDisputedTransaction(transaction),
                reasonId,
                comment,
                submittedAt);

            disputes.Add(dispute);
            return dispute;
        }
    }

    private static void Approve(Dispute dispute, DateTimeOffset approvedAt) =>
        dispute.MarkSentToScheme(SchemePayloadFactory.Create(dispute, approvedAt), Agent, approvedAt);
}
