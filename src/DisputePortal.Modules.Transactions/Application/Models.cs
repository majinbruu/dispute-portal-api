using DisputePortal.Modules.Transactions.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Transactions.Application;

internal sealed record DisputeEligibilityDto(bool CanDispute, DisputeEligibility Code, string Message, DateOnly LastDisputeDate);

internal sealed record DisputeLinkDto(Guid Id, string Reference, string Status);

internal sealed record DebitOrderDto(
    Guid Id,
    string TransactionId,
    string EndToEndId,
    string CreditorName,
    string CreditorShortName,
    string StatementText,
    decimal Amount,
    string Currency,
    DateOnly ActionDate,
    SequenceType SequenceType,
    DebitOrderStatus Status,
    string? StatusReasonCode,
    string? StatusReason,
    string DebtorAccountMasked,
    Guid? MandateId,
    DebitOrderType MandateType,
    string? MandateReference,
    string ContractReference,
    DisputeEligibilityDto Eligibility,
    DisputeLinkDto? Dispute);

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

/// <summary>Query string for the debit order list.</summary>
internal sealed class DebitOrderListQuery
{
    [FromQuery(Name = "status")]
    public DebitOrderStatus? Status { get; init; }

    [FromQuery(Name = "mandateType")]
    public DebitOrderType? MandateType { get; init; }

    /// <summary>Earliest action date, inclusive.</summary>
    [FromQuery(Name = "from")]
    public DateOnly? From { get; init; }

    /// <summary>Latest action date, inclusive.</summary>
    [FromQuery(Name = "to")]
    public DateOnly? To { get; init; }

    /// <summary>One of: -actionDate (default), actionDate, -amount, amount.</summary>
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

internal enum DebitOrderSort
{
    ActionDateDescending,
    ActionDateAscending,
    AmountDescending,
    AmountAscending,
}

internal sealed record DebitOrderFilter(
    DebitOrderStatus? Status,
    DebitOrderType? MandateType,
    DateOnly? From,
    DateOnly? To,
    DebitOrderSort Sort);

internal static class DebitOrderSorts
{
    public static readonly IReadOnlyDictionary<string, DebitOrderSort> ByName = new Dictionary<string, DebitOrderSort>(StringComparer.Ordinal)
    {
        ["-actionDate"] = DebitOrderSort.ActionDateDescending,
        ["actionDate"] = DebitOrderSort.ActionDateAscending,
        ["-amount"] = DebitOrderSort.AmountDescending,
        ["amount"] = DebitOrderSort.AmountAscending,
    };

    public static DebitOrderSort Parse(string? value) =>
        value is not null && ByName.TryGetValue(value, out var sort) ? sort : DebitOrderSort.ActionDateDescending;
}
