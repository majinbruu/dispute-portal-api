namespace DisputePortal.Modules.Transactions.Contracts;

/// <summary>The values of <see cref="TransactionForDispute.Type"/>.</summary>
public static class TransactionTypeNames
{
    public const string DebitOrder = "DebitOrder";
    public const string Card = "Card";
}

/// <summary>
/// What another module needs to know about a transaction in order to raise a dispute against it.
/// Exactly one of <see cref="DebitOrder"/> and <see cref="Card"/> is set, matching <see cref="Type"/>.
/// </summary>
/// <param name="SchemeReference">How the payment scheme identifies the transaction: the pacs.003 TxId of a debit order, or the acquirer reference number of a card transaction.</param>
/// <param name="Amount">What a dispute would claim. For a partly refunded card purchase this is what is still outstanding.</param>
/// <param name="TransactionDate">The action date of a debit order, or the day the card was used.</param>
public sealed record TransactionForDispute(
    Guid Id,
    Guid CustomerId,
    string Type,
    string SchemeReference,
    decimal Amount,
    string Currency,
    DateOnly TransactionDate,
    string CounterpartyName,
    string AccountMasked,
    DebitOrderForDispute? DebitOrder,
    CardForDispute? Card,
    string? IneligibilityCode,
    string? IneligibilityMessage)
{
    /// <summary>True when the rules for the transaction type and the dispute window all allow a dispute.</summary>
    public bool IsEligible => IneligibilityCode is null;
}

public sealed record DebitOrderForDispute(string EndToEndId, string MandateType, string MandateReference);

public sealed record CardForDispute(string Scheme, string EntryMode, string Verification, DateOnly? PostingDate);

/// <summary>Read access to transactions for other modules.</summary>
public interface ITransactionDirectory
{
    /// <summary>Returns the transaction if it exists and belongs to the customer, otherwise null.</summary>
    Task<TransactionForDispute?> FindAsync(Guid transactionId, Guid customerId, CancellationToken cancellationToken);
}

public sealed record TransactionDisputeStatus(Guid DisputeId, string Reference, string Status);

/// <summary>
/// A port this module needs: which transactions already have a dispute. The Disputes module implements it,
/// so the dependency points from Disputes to Transactions and never the other way.
/// </summary>
public interface IDisputeStatusProvider
{
    Task<IReadOnlyDictionary<Guid, TransactionDisputeStatus>> GetForTransactionsAsync(
        IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken);
}
