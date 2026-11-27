using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Domain;

/// <summary>
/// Simplified, portal-side dispute rules for card transactions. The chargeback rules of the card schemes are far
/// more detailed; the portal only decides whether a dispute may be raised at all.
/// </summary>
internal static class CardDisputabilityPolicy
{
    /// <summary>
    /// 120 calendar days, counted from the first calendar day after the transaction was posted,
    /// up to and including day 120. The same counting as the debit order window.
    /// </summary>
    public const int DisputeWindowDays = 120;

    /// <summary>Decides whether a card transaction can be disputed. Called once, when the transaction is recorded.</summary>
    public static DisputabilityDecision Evaluate(CardTransactionKind kind, CardSettlementStatus status, Money amount, Money refundedAmount)
    {
        if (kind != CardTransactionKind.Purchase)
        {
            return new(false, DisputabilityReason.NotAPurchase);
        }

        // Pending, declined and reversed transactions took no money from the account.
        if (status != CardSettlementStatus.Settled)
        {
            return new(false, DisputabilityReason.NotSettled);
        }

        // A partly refunded purchase can still be disputed for the rest.
        if (!amount.IsGreaterThan(refundedAmount))
        {
            return new(false, DisputabilityReason.FullyRefunded);
        }

        return new(true, DisputabilityReason.CardPurchase);
    }

    /// <summary>Day 1 is the day after posting, so day 120 is the posting date plus 120 days.</summary>
    public static DateOnly LastDisputeDate(DateOnly postingDate) => postingDate.AddDays(DisputeWindowDays);
}
