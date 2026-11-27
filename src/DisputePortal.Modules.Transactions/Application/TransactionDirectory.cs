using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Application;

/// <summary>Implements the contract other modules use to look up a transaction before disputing it.</summary>
internal sealed class TransactionDirectory(ITransactionStore store, TimeProvider timeProvider) : ITransactionDirectory
{
    public async Task<TransactionForDispute?> FindAsync(Guid transactionId, Guid customerId, CancellationToken cancellationToken)
    {
        var transaction = await store.FindAsync(transactionId, customerId, cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        // Whether a dispute already exists is the Disputes module's knowledge, so it is not evaluated here.
        var eligibility = transaction.EvaluateEligibility(SouthAfricanTime.Today(timeProvider), alreadyDisputed: false);
        var eligible = eligibility == DisputeEligibility.Eligible;
        var ineligibilityCode = eligible ? null : ToErrorCode(eligibility);
        var ineligibilityMessage = eligible ? null : EligibilityMessages.For(eligibility, transaction);

        return transaction switch
        {
            DebitOrder debitOrder => new TransactionForDispute(
                debitOrder.Id,
                debitOrder.CustomerId,
                TransactionTypeNames.DebitOrder,
                debitOrder.TransactionId,
                debitOrder.Amount,
                debitOrder.Currency,
                debitOrder.EffectiveDate,
                debitOrder.CounterpartyName,
                debitOrder.AccountMasked,
                new DebitOrderForDispute(debitOrder.EndToEndId, debitOrder.MandateType.ToString(), debitOrder.MandateReference),
                Card: null,
                ineligibilityCode,
                ineligibilityMessage),
            CardTransaction card => new TransactionForDispute(
                card.Id,
                card.CustomerId,
                TransactionTypeNames.Card,
                card.AcquirerReferenceNumber,
                card.OutstandingAmount,
                card.Currency,
                card.TransactionDate,
                card.CounterpartyName,
                card.AccountMasked,
                DebitOrder: null,
                new CardForDispute(card.Scheme.ToString(), card.EntryMode.ToString(), card.Verification.ToString(), card.PostingDate),
                ineligibilityCode,
                ineligibilityMessage),
            _ => throw new InvalidOperationException($"Transaction type {transaction.Type} cannot be disputed."),
        };
    }

    private static string ToErrorCode(DisputeEligibility eligibility) => eligibility switch
    {
        DisputeEligibility.WithinMandateTerms => "within_mandate_terms",
        DisputeEligibility.CollectionRejected => "collection_rejected",
        DisputeEligibility.NotAPurchase => "not_a_purchase",
        DisputeEligibility.NotSettled => "not_settled",
        DisputeEligibility.FullyRefunded => "fully_refunded",
        DisputeEligibility.DisputeWindowClosed => "dispute_window_closed",
        _ => "not_disputable",
    };
}
