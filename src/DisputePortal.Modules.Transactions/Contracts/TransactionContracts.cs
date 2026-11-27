namespace DisputePortal.Modules.Transactions.Contracts;

/// <summary>What another module needs to know about a debit order in order to raise a dispute against it.</summary>
public sealed record DebitOrderForDispute(
    Guid Id,
    Guid CustomerId,
    string TransactionId,
    string EndToEndId,
    decimal Amount,
    string Currency,
    DateOnly ActionDate,
    string MandateType,
    string MandateReference,
    string CreditorName,
    string DebtorAccountMasked,
    string? IneligibilityCode,
    string? IneligibilityMessage)
{
    /// <summary>True when the mandate rules, the collection outcome and the dispute window all allow a dispute.</summary>
    public bool IsEligible => IneligibilityCode is null;
}

/// <summary>Read access to debit orders for other modules.</summary>
public interface IDebitOrderDirectory
{
    /// <summary>Returns the debit order if it exists and belongs to the customer, otherwise null.</summary>
    Task<DebitOrderForDispute?> FindAsync(Guid debitOrderId, Guid customerId, CancellationToken cancellationToken);
}

public sealed record DebitOrderDisputeStatus(Guid DisputeId, string Reference, string Status);

/// <summary>
/// A port this module needs: which debit orders already have a dispute. The Disputes module implements it,
/// so the dependency points from Disputes to Transactions and never the other way.
/// </summary>
public interface IDisputeStatusProvider
{
    Task<IReadOnlyDictionary<Guid, DebitOrderDisputeStatus>> GetForDebitOrdersAsync(
        IReadOnlyCollection<Guid> debitOrderIds, CancellationToken cancellationToken);
}
