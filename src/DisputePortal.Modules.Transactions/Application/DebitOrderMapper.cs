using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.Modules.Transactions.Domain;

namespace DisputePortal.Modules.Transactions.Application;

internal static class DebitOrderMapper
{
    public static DebitOrderDto ToDto(DebitOrder debitOrder, DateOnly today, DebitOrderDisputeStatus? dispute)
    {
        var eligibility = debitOrder.EvaluateEligibility(today, alreadyDisputed: dispute is not null);

        return new DebitOrderDto(
            debitOrder.Id,
            debitOrder.TransactionId,
            debitOrder.EndToEndId,
            debitOrder.CounterpartyName,
            debitOrder.CreditorShortName,
            debitOrder.StatementText,
            debitOrder.Amount,
            debitOrder.Currency,
            debitOrder.EffectiveDate,
            debitOrder.SequenceType,
            debitOrder.Status,
            debitOrder.StatusReasonCode,
            StatusReasons.Describe(debitOrder.StatusReasonCode),
            debitOrder.AccountMasked,
            debitOrder.MandateId,
            debitOrder.MandateType,
            debitOrder.MandateReference,
            new DisputeEligibilityDto(
                eligibility == DisputeEligibility.Eligible,
                eligibility,
                EligibilityMessages.For(eligibility, debitOrder),
                debitOrder.LastDisputeDate),
            dispute is null ? null : new DisputeLinkDto(dispute.DisputeId, dispute.Reference, dispute.Status));
    }

    public static MandateDto ToDto(Mandate mandate) => new(
        mandate.Id,
        mandate.MandateReference,
        mandate.ContractReference,
        mandate.Type,
        mandate.CreditorName,
        mandate.CreditorShortName,
        mandate.InstalmentAmount,
        mandate.MaximumCollectionAmount,
        mandate.Currency,
        mandate.Frequency,
        mandate.CollectionDay,
        mandate.FirstCollectionDate,
        mandate.DateAdjustmentAllowed,
        mandate.TrackingEnabled,
        mandate.AuthenticationType,
        mandate.AuthenticatedAt,
        mandate.Status);
}

internal static class EligibilityMessages
{
    public static string For(DisputeEligibility eligibility, Transaction transaction)
    {
        var isCard = transaction.Type == TransactionType.Card;
        var name = isCard ? "card transaction" : "debit order";

        return eligibility switch
        {
            DisputeEligibility.Eligible =>
                $"You can dispute this {name} until {transaction.LastDisputeDate:d MMMM yyyy}.",
            DisputeEligibility.WithinMandateTerms =>
                "This debit order matches the DebiCheck mandate you approved, so it cannot be disputed. Contact the service provider to query it.",
            DisputeEligibility.CollectionRejected =>
                "This debit order was rejected, so no money left your account and there is nothing to dispute.",
            DisputeEligibility.NotAPurchase =>
                "Only card purchases can be disputed here. Contact the bank about a cash withdrawal.",
            DisputeEligibility.NotSettled =>
                "Only a settled card transaction can be disputed.",
            DisputeEligibility.FullyRefunded =>
                "The merchant has already refunded this card transaction in full.",
            DisputeEligibility.DisputeWindowClosed => isCard
                ? $"The {CardDisputabilityPolicy.DisputeWindowDays}-day dispute period ended on {transaction.LastDisputeDate:d MMMM yyyy}. Contact the merchant to resolve it."
                : $"The {DebitOrderDisputabilityPolicy.DisputeWindowDays}-day dispute period ended on {transaction.LastDisputeDate:d MMMM yyyy}. Contact the service provider to resolve it.",
            DisputeEligibility.AlreadyDisputed =>
                $"You have already disputed this {name}.",
            _ => throw new ArgumentOutOfRangeException(nameof(eligibility), eligibility, null),
        };
    }
}

/// <summary>Customer wording for the ISO 20022 status reason codes we expect on a rejected collection.</summary>
internal static class StatusReasons
{
    public static string? Describe(string? code) => code switch
    {
        null => null,
        "AM04" => "Insufficient funds",
        "AC04" => "Account closed",
        "AC06" => "Account blocked",
        "MD01" => "No mandate",
        _ => "Collection rejected",
    };
}
