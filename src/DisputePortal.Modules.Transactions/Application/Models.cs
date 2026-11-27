using DisputePortal.Modules.Transactions.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DisputePortal.Modules.Transactions.Application;

internal sealed record DisputeEligibilityDto(bool CanDispute, DisputeEligibility Code, string Message, DateOnly LastDisputeDate);

internal sealed record DisputeLinkDto(Guid Id, string Reference, string Status);

/// <summary>
/// A transaction as the list and the detail return it: the fields every type shares, plus the details of its
/// own type. Exactly one of <see cref="DebitOrder"/> and <see cref="Card"/> is set, matching <see cref="Type"/>.
/// </summary>
/// <param name="CounterpartyName">The creditor of a debit order, or the merchant of a card transaction.</param>
/// <param name="EffectiveDate">The date the dispute window counts from: the action date of a debit order, or the posting date of a card transaction (its transaction date while it is not posted).</param>
internal sealed record TransactionDto(
    Guid Id,
    TransactionType Type,
    string CounterpartyName,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate,
    string AccountMasked,
    DisputeEligibilityDto Eligibility,
    DisputeLinkDto? Dispute,
    DebitOrderDetailDto? DebitOrder,
    CardDetailDto? Card);

/// <param name="MandateType">DebiCheck, Registered or Eft. An EFT debit order has no mandate, so its mandate ID and reference are null.</param>
internal sealed record DebitOrderDetailDto(
    string TransactionId,
    string EndToEndId,
    string CreditorShortName,
    string StatementText,
    SequenceType SequenceType,
    DebitOrderStatus Status,
    string? StatusReasonCode,
    string? StatusReason,
    DebitOrderType MandateType,
    Guid? MandateId,
    string? MandateReference,
    string ContractReference);

/// <param name="PostingDate">Null while the transaction is pending, and when it never settled.</param>
internal sealed record CardDetailDto(
    string CardNumberMasked,
    CardScheme Scheme,
    CardTransactionKind Kind,
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
    decimal RefundedAmount);

internal sealed record MandateDto(
    Guid Id,
    string MandateReference,
    string ContractReference,
    MandateType Type,
    string CreditorName,
    string CreditorShortName,
    decimal InstalmentAmount,
    decimal MaximumCollectionAmount,
    string Currency,
    CollectionFrequency Frequency,
    int CollectionDay,
    DateOnly FirstCollectionDate,
    bool DateAdjustmentAllowed,
    bool TrackingEnabled,
    string? AuthenticationType,
    DateTimeOffset? AuthenticatedAt,
    MandateStatus Status);

/// <summary>Query string for the transaction list. One list never mixes transaction types, or kinds of debit order.</summary>
internal sealed class TransactionListQuery
{
    [FromQuery(Name = "type")]
    [BindRequired]
    public TransactionType Type { get; init; }

    /// <summary>Required for debit orders: DebiCheck, Registered or Eft.</summary>
    [FromQuery(Name = "mandateType")]
    public DebitOrderType? MandateType { get; init; }

    /// <summary>Debit orders only.</summary>
    [FromQuery(Name = "debitOrderStatus")]
    public DebitOrderStatus? DebitOrderStatus { get; init; }

    /// <summary>Card transactions only.</summary>
    [FromQuery(Name = "cardStatus")]
    public CardSettlementStatus? CardStatus { get; init; }

    /// <summary>Earliest effective date, inclusive.</summary>
    [FromQuery(Name = "from")]
    public DateOnly? From { get; init; }

    /// <summary>Latest effective date, inclusive.</summary>
    [FromQuery(Name = "to")]
    public DateOnly? To { get; init; }

    /// <summary>One of: -effectiveDate (default), effectiveDate, -amount, amount.</summary>
    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

/// <summary>Query string for the mandate list.</summary>
internal sealed class MandateListQuery
{
    [FromQuery(Name = "type")]
    public MandateType? Type { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

internal enum TransactionSort
{
    EffectiveDateDescending,
    EffectiveDateAscending,
    AmountDescending,
    AmountAscending,
}

internal sealed record TransactionFilter(
    TransactionType Type,
    DebitOrderType? MandateType,
    DebitOrderStatus? DebitOrderStatus,
    CardSettlementStatus? CardStatus,
    DateOnly? From,
    DateOnly? To,
    TransactionSort Sort);

internal static class TransactionSorts
{
    public static readonly IReadOnlyDictionary<string, TransactionSort> ByName = new Dictionary<string, TransactionSort>(StringComparer.Ordinal)
    {
        ["-effectiveDate"] = TransactionSort.EffectiveDateDescending,
        ["effectiveDate"] = TransactionSort.EffectiveDateAscending,
        ["-amount"] = TransactionSort.AmountDescending,
        ["amount"] = TransactionSort.AmountAscending,
    };

    public static TransactionSort Parse(string? value) =>
        value is not null && ByName.TryGetValue(value, out var sort) ? sort : TransactionSort.EffectiveDateDescending;
}
