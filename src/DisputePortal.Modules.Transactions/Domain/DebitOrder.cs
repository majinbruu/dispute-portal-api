using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Domain;

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

/// <summary>A debit order collection against the customer's account. Its action date is the transaction's effective date.</summary>
internal sealed class DebitOrder : Transaction
{
    private DebitOrder()
    {
    }

    private DebitOrder(Guid id, Mandate mandate, CollectionRequest request, CollectionResponse response, DateTimeOffset now)
        : base(
            id,
            mandate.CustomerId,
            TransactionType.DebitOrder,
            request.DebtorAccountNumber,
            mandate.CreditorName,
            request.Amount,
            request.ActionDate,
            DebitOrderDisputabilityPolicy.Evaluate(mandate, request.Amount, request.ActionDate, response.Accepted),
            DebitOrderDisputabilityPolicy.LastDisputeDate(request.ActionDate),
            now)
    {
        MandateId = mandate.Id;
        MandateType = mandate.Type;
        MandateReference = mandate.MandateReference;
        TransactionId = request.TransactionId;
        EndToEndId = request.EndToEndId;
        SequenceType = request.SequenceType;
        CreditorShortName = mandate.CreditorShortName;
        StatementText = request.StatementText;
        Status = response.Accepted ? DebitOrderStatus.Accepted : DebitOrderStatus.Rejected;
        StatusReasonCode = response.Accepted ? null : response.ReasonCode;
    }

    public Guid MandateId { get; private set; }

    public MandateType MandateType { get; private set; }

    public string MandateReference { get; private set; } = null!;

    /// <summary>pacs.003 TxId.</summary>
    public string TransactionId { get; private set; } = null!;

    /// <summary>pacs.003 EndToEndId.</summary>
    public string EndToEndId { get; private set; } = null!;

    public SequenceType SequenceType { get; private set; }

    /// <summary>The abbreviated creditor name that prints on the customer's statement.</summary>
    public string CreditorShortName { get; private set; } = null!;

    public string StatementText { get; private set; } = null!;

    public DebitOrderStatus Status { get; private set; }

    public string? StatusReasonCode { get; private set; }

    /// <summary>
    /// Records a collection request and its response. Disputability is calculated here, up front,
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

        return new DebitOrder(id, mandate, request, response, now);
    }
}
