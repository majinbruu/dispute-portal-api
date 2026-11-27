using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Domain;

/// <summary>
/// Simplified, portal-side dispute rules for debit orders. In a real bank the authoritative pacs.003 validation
/// and dispute calculation live in downstream payment systems and the portal displays their result.
/// </summary>
internal static class DebitOrderDisputabilityPolicy
{
    /// <summary>
    /// PASA Debit Order Dispute Enhancements, effective 13 April 2026: 60 calendar days,
    /// counted from the first calendar day after a successful debit, up to and including day 60.
    /// </summary>
    public const int DisputeWindowDays = 60;

    /// <summary>Decides whether a collection can be disputed. Called once, when the collection is recorded.</summary>
    public static DisputabilityDecision Evaluate(Mandate mandate, Money amount, DateOnly actionDate, bool accepted)
    {
        // A rejected collection debited nothing, so there is nothing to dispute.
        if (!accepted)
        {
            return new(false, DisputabilityReason.CollectionRejected);
        }

        // A registered mandate was never confirmed by the customer with the bank, so its collections are disputable.
        if (mandate.Type == MandateType.Registered)
        {
            return new(true, DisputabilityReason.RegisteredMandate);
        }

        // A DebiCheck mandate was authenticated by the customer. A collection is disputable only if it breaks those terms.
        if (amount.IsGreaterThan(new Money(mandate.MaximumCollectionAmount, mandate.Currency)))
        {
            return new(true, DisputabilityReason.AmountAboveMandateMaximum);
        }

        if (actionDate.Day != mandate.CollectionDay && !mandate.DateAdjustmentAllowed)
        {
            return new(true, DisputabilityReason.DateOutsideMandateTerms);
        }

        return new(false, DisputabilityReason.WithinMandateTerms);
    }

    /// <summary>Day 1 is the day after the debit, so day 60 is the action date plus 60 days.</summary>
    public static DateOnly LastDisputeDate(DateOnly actionDate) => actionDate.AddDays(DisputeWindowDays);
}
