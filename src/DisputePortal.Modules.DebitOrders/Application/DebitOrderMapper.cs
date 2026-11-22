using DisputePortal.Modules.DebitOrders.Contracts;
using DisputePortal.Modules.DebitOrders.Domain;

namespace DisputePortal.Modules.DebitOrders.Application;

internal static class DebitOrderMapper
{
    public static DebitOrderDto ToDto(DebitOrder debitOrder, DateOnly today, DebitOrderDisputeStatus? dispute)
    {
        var eligibility = debitOrder.EvaluateEligibility(today, alreadyDisputed: dispute is not null);

        return new DebitOrderDto(
            debitOrder.Id,
            debitOrder.TransactionId,
            debitOrder.EndToEndId,
            debitOrder.CreditorName,
            debitOrder.CreditorShortName,
            debitOrder.StatementText,
            debitOrder.Amount,
            debitOrder.Currency,
            debitOrder.ActionDate,
            debitOrder.SequenceType,
            debitOrder.Status,
            debitOrder.StatusReasonCode,
            StatusReasons.Describe(debitOrder.StatusReasonCode),
            debitOrder.DebtorAccountMasked,
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
    public static string For(DisputeEligibility eligibility, DebitOrder debitOrder) => eligibility switch
    {
        DisputeEligibility.Eligible =>
            $"You can dispute this debit order until {debitOrder.LastDisputeDate:d MMMM yyyy}.",
        DisputeEligibility.WithinMandateTerms =>
            "This debit order matches the DebiCheck mandate you approved, so it cannot be disputed. Contact the service provider to query it.",
        DisputeEligibility.CollectionFailed =>
            "This debit order was not successful, so no money left your account and there is nothing to dispute.",
        DisputeEligibility.DisputeWindowClosed =>
            $"The 60-day dispute period ended on {debitOrder.LastDisputeDate:d MMMM yyyy}. Contact the service provider to resolve it.",
        DisputeEligibility.AlreadyDisputed =>
            "You have already disputed this debit order.",
        _ => throw new ArgumentOutOfRangeException(nameof(eligibility), eligibility, null),
    };
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
