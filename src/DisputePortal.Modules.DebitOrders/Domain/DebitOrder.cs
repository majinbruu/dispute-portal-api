using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.DebitOrders.Domain;

/// <summary>The fields we keep from a pacs.003 collection request.</summary>
internal sealed record CollectionRequest(
    string TransactionId,
    string EndToEndId,
    Money Amount,
    DateOnly ActionDate,
    SequenceType SequenceType,
    string StatementText,
    string DebtorAccountNumber);

/// <summary>The fields we keep from the pacs.002 status report that answers a collection request.</summary>
internal sealed record CollectionResponse(bool Accepted, string? ReasonCode = null)
{
    public static CollectionResponse AcceptedResponse { get; } = new(true);

    public static CollectionResponse Rejected(string reasonCode) => new(false, reasonCode);
}

/// <summary>A debit order collection against the customer's account.</summary>
internal sealed class DebitOrder
{
    private DebitOrder()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid MandateId { get; private set; }

    public MandateType MandateType { get; private set; }

    public string MandateReference { get; private set; } = null!;

    /// <summary>pacs.003 TxId.</summary>
    public string TransactionId { get; private set; } = null!;

    /// <summary>pacs.003 EndToEndId.</summary>
    public string EndToEndId { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>pacs.003 ReqdColltnDt.</summary>
    public DateOnly ActionDate { get; private set; }

    public SequenceType SequenceType { get; private set; }

    public string CreditorName { get; private set; } = null!;

    public string CreditorShortName { get; private set; } = null!;

    public string StatementText { get; private set; } = null!;

    /// <summary>Only the last four digits are stored. The full account number never reaches this table or the logs.</summary>
    public string DebtorAccountMasked { get; private set; } = null!;

    public DebitOrderStatus Status { get; private set; }

    public string? StatusReasonCode { get; private set; }

    public bool IsDisputableUnderMandate { get; private set; }

    public DisputabilityReason DisputabilityReason { get; private set; }

    public DateOnly LastDisputeDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Records a collection request and its response. Disputability under the mandate is calculated here, up front,
    /// which is how the customer can be shown whether a debit order is disputable.
    /// </summary>
    public static DebitOrder Record(Guid id, Mandate mandate, CollectionRequest request, CollectionResponse response, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TransactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EndToEndId);

        if (!response.Accepted && string.IsNullOrWhiteSpace(response.ReasonCode))
        {
            throw new ArgumentException("A rejected collection must carry a reason code.", nameof(response));
        }

        var decision = DisputabilityPolicy.Evaluate(mandate, request.Amount, request.ActionDate);

        return new DebitOrder
        {
            Id = id,
            CustomerId = mandate.CustomerId,
            MandateId = mandate.Id,
            MandateType = mandate.Type,
            MandateReference = mandate.MandateReference,
            TransactionId = request.TransactionId,
            EndToEndId = request.EndToEndId,
            Amount = request.Amount.Amount,
            Currency = request.Amount.Currency,
            ActionDate = request.ActionDate,
            SequenceType = request.SequenceType,
            CreditorName = mandate.CreditorName,
            CreditorShortName = mandate.CreditorShortName,
            StatementText = request.StatementText,
            DebtorAccountMasked = AccountNumber.Mask(request.DebtorAccountNumber),
            Status = response.Accepted ? DebitOrderStatus.Successful : DebitOrderStatus.Failed,
            StatusReasonCode = response.Accepted ? null : response.ReasonCode,
            IsDisputableUnderMandate = decision.IsDisputable,
            DisputabilityReason = decision.Reason,
            LastDisputeDate = DisputabilityPolicy.LastDisputeDate(request.ActionDate),
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Whether the customer can dispute this debit order today. The mandate rule was fixed when the collection
    /// was recorded; the remaining checks depend on time and on whether a dispute already exists.
    /// </summary>
    public DisputeEligibility EvaluateEligibility(DateOnly today, bool alreadyDisputed)
    {
        if (Status == DebitOrderStatus.Failed)
        {
            return DisputeEligibility.CollectionFailed;
        }

        if (alreadyDisputed)
        {
            return DisputeEligibility.AlreadyDisputed;
        }

        if (!IsDisputableUnderMandate)
        {
            return DisputeEligibility.WithinMandateTerms;
        }

        return today > LastDisputeDate ? DisputeEligibility.DisputeWindowClosed : DisputeEligibility.Eligible;
    }
}

internal static class AccountNumber
{
    public static string Mask(string accountNumber)
    {
        var digits = new string((accountNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
        {
            throw new ArgumentException("An account number must have at least four digits.", nameof(accountNumber));
        }

        return new string('*', digits.Length - 4) + digits[^4..];
    }
}
