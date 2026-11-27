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

/// <summary>
/// Who is collecting. A DebiCheck or Registered Mandate collection takes this from its mandate.
/// An EFT collection has no mandate at the bank, so the collection request itself has to say.
/// </summary>
internal sealed record CollectingCreditor(string Name, string ShortName, string ContractReference);

/// <summary>A debit order collection against the customer's account. Its action date is the transaction's effective date.</summary>
internal sealed class DebitOrder : Transaction
{
    private DebitOrder()
    {
    }

    private DebitOrder(
        Guid id,
        Guid customerId,
        DebitOrderType mandateType,
        Mandate? mandate,
        CollectingCreditor creditor,
        CollectionRequest request,
        CollectionResponse response,
        DisputabilityDecision decision,
        DateTimeOffset now)
        : base(
            id,
            customerId,
            TransactionType.DebitOrder,
            request.DebtorAccountNumber,
            creditor.Name,
            request.Amount,
            request.ActionDate,
            decision,
            DebitOrderDisputabilityPolicy.LastDisputeDate(request.ActionDate),
            now)
    {
        MandateType = mandateType;
        MandateId = mandate?.Id;
        MandateReference = mandate?.MandateReference;
        ContractReference = creditor.ContractReference;
        CreditorShortName = creditor.ShortName;
        TransactionId = request.TransactionId;
        EndToEndId = request.EndToEndId;
        SequenceType = request.SequenceType;
        StatementText = request.StatementText;
        Status = response.Accepted ? DebitOrderStatus.Accepted : DebitOrderStatus.Rejected;
        StatusReasonCode = response.Accepted ? null : response.ReasonCode;
    }

    /// <summary>Which kind of debit order this is. Only DebiCheck and Registered have a mandate.</summary>
    public DebitOrderType MandateType { get; private set; }

    /// <summary>Empty for an EFT debit order.</summary>
    public Guid? MandateId { get; private set; }

    /// <summary>Empty for an EFT debit order.</summary>
    public string? MandateReference { get; private set; }

    /// <summary>The creditor's reference for the agreement with the customer, such as a policy or account number.</summary>
    public string ContractReference { get; private set; } = null!;

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
    /// Records a collection made under a mandate, and its response. Disputability is calculated here, up front,
    /// which is how the customer can be shown whether a debit order is disputable.
    /// </summary>
    public static DebitOrder Record(Guid id, Mandate mandate, CollectionRequest request, CollectionResponse response, DateTimeOffset now)
    {
        Validate(request, response);

        return new DebitOrder(
            id,
            mandate.CustomerId,
            mandate.Type == Domain.MandateType.DebiCheck ? DebitOrderType.DebiCheck : DebitOrderType.Registered,
            mandate,
            new CollectingCreditor(mandate.CreditorName, mandate.CreditorShortName, mandate.ContractReference),
            request,
            response,
            DebitOrderDisputabilityPolicy.Evaluate(mandate, request.Amount, request.ActionDate, response.Accepted),
            now);
    }

    /// <summary>
    /// Records an EFT collection and its response. The customer never authenticated it and the bank holds no
    /// mandate for it, so there are no mandate terms to check it against.
    /// </summary>
    public static DebitOrder RecordEft(
        Guid id, Guid customerId, CollectingCreditor creditor, CollectionRequest request, CollectionResponse response, DateTimeOffset now)
    {
        Validate(request, response);
        ArgumentException.ThrowIfNullOrWhiteSpace(creditor.ShortName);
        ArgumentException.ThrowIfNullOrWhiteSpace(creditor.ContractReference);

        return new DebitOrder(
            id,
            customerId,
            DebitOrderType.Eft,
            mandate: null,
            creditor,
            request,
            response,
            DebitOrderDisputabilityPolicy.EvaluateWithoutMandate(response.Accepted),
            now);
    }

    private static void Validate(CollectionRequest request, CollectionResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TransactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EndToEndId);

        if (!response.Accepted && string.IsNullOrWhiteSpace(response.ReasonCode))
        {
            throw new ArgumentException("A rejected collection must carry a reason code.", nameof(response));
        }
    }
}
