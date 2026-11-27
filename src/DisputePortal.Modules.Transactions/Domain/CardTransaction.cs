using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Domain;

/// <summary>What the card system reports about one card transaction.</summary>
internal sealed record CardTransactionDetails(
    string CardNumber,
    CardScheme Scheme,
    CardTransactionKind Kind,
    Money Amount,
    string MerchantName,
    string MerchantCity,
    string MerchantCountry,
    string MerchantCategoryCode,
    DateOnly TransactionDate,
    DateOnly? PostingDate,
    string AuthorisationCode,
    string AcquirerReferenceNumber,
    CardEntryMode EntryMode,
    CardholderVerification Verification,
    CardSettlementStatus Status,
    Money RefundedAmount);

/// <summary>
/// A card transaction on the customer's account. The merchant is the transaction's counterparty. The effective
/// date is the posting date, or the transaction date while it has not been posted.
/// </summary>
internal sealed class CardTransaction : Transaction
{
    private CardTransaction()
    {
    }

    private CardTransaction(Guid id, Guid customerId, string accountNumber, CardTransactionDetails details, DateTimeOffset now)
        : base(
            id,
            customerId,
            TransactionType.Card,
            accountNumber,
            details.MerchantName,
            details.Amount,
            details.PostingDate ?? details.TransactionDate,
            CardDisputabilityPolicy.Evaluate(details.Kind, details.Status, details.Amount, details.RefundedAmount),
            CardDisputabilityPolicy.LastDisputeDate(details.PostingDate ?? details.TransactionDate),
            now)
    {
        CardNumberMasked = NumberMask.KeepLastFour(details.CardNumber);
        Scheme = details.Scheme;
        Kind = details.Kind;
        MerchantCity = details.MerchantCity;
        MerchantCountry = details.MerchantCountry;
        MerchantCategoryCode = details.MerchantCategoryCode;
        TransactionDate = details.TransactionDate;
        PostingDate = details.PostingDate;
        AuthorisationCode = details.AuthorisationCode;
        AcquirerReferenceNumber = details.AcquirerReferenceNumber;
        EntryMode = details.EntryMode;
        Verification = details.Verification;
        Status = details.Status;
        RefundedAmount = details.RefundedAmount.Amount;
    }

    /// <summary>Only the last four digits are stored. The full card number never reaches the database or the logs.</summary>
    public string CardNumberMasked { get; private set; } = null!;

    public CardScheme Scheme { get; private set; }

    public CardTransactionKind Kind { get; private set; }

    public string MerchantCity { get; private set; } = null!;

    /// <summary>ISO 3166 two-letter country code.</summary>
    public string MerchantCountry { get; private set; } = null!;

    /// <summary>The four-digit code the card schemes use for the merchant's line of business.</summary>
    public string MerchantCategoryCode { get; private set; } = null!;

    /// <summary>The day the card was used.</summary>
    public DateOnly TransactionDate { get; private set; }

    /// <summary>The day the transaction was posted to the account. Empty while it is pending, and when it never settled.</summary>
    public DateOnly? PostingDate { get; private set; }

    public string AuthorisationCode { get; private set; } = null!;

    /// <summary>The reference the card scheme knows the transaction by. A chargeback quotes it.</summary>
    public string AcquirerReferenceNumber { get; private set; } = null!;

    public CardEntryMode EntryMode { get; private set; }

    public CardholderVerification Verification { get; private set; }

    public CardSettlementStatus Status { get; private set; }

    /// <summary>What the merchant has already paid back.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>What a dispute can still claim: the amount less what the merchant has already refunded.</summary>
    public decimal OutstandingAmount => Amount - RefundedAmount;

    /// <summary>Records a card transaction. Disputability is decided here, up front, as it is for a debit order.</summary>
    public static CardTransaction Record(Guid id, Guid customerId, string accountNumber, CardTransactionDetails details, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(details.MerchantCity);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.AuthorisationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.AcquirerReferenceNumber);

        if (details.MerchantCountry is not { Length: 2 })
        {
            throw new ArgumentException("The merchant country must be a two-letter ISO 3166 code.", nameof(details));
        }

        if (details.MerchantCategoryCode is not { Length: 4 } || !details.MerchantCategoryCode.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("The merchant category code must be four digits.", nameof(details));
        }

        if (details.Status == CardSettlementStatus.Settled && details.PostingDate is null)
        {
            throw new ArgumentException("A settled card transaction must have a posting date.", nameof(details));
        }

        if (details.RefundedAmount.IsGreaterThan(details.Amount))
        {
            throw new ArgumentException("The refunded amount cannot be more than the transaction amount.", nameof(details));
        }

        return new CardTransaction(id, customerId, accountNumber, details, now);
    }
}
