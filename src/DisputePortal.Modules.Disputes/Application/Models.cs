using DisputePortal.Modules.Disputes.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>How the customer thinks about a dispute: still in progress, or finished.</summary>
internal enum DisputeState
{
    Pending,
    Completed,
}

/// <summary>Mandate type as a query parameter for the dispute reasons endpoint.</summary>
internal enum MandateKind
{
    DebiCheck,
    Registered,
}

internal sealed record SubmitDisputeRequest(Guid DebitOrderId, string ReasonCode, string? Comment);

internal sealed record DisputeReasonDto(string Code, string Description);

internal sealed record DisputeEventDto(DisputeStatus Status, string Description, DateTimeOffset OccurredAt);

internal sealed record DisputeSummaryDto(
    Guid Id,
    string Reference,
    Guid DebitOrderId,
    string CreditorName,
    decimal Amount,
    string Currency,
    DateOnly ActionDate,
    string ReasonCode,
    string ReasonDescription,
    DisputeStatus Status,
    DisputeState State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt);

internal sealed record DisputeDetailDto(
    Guid Id,
    string Reference,
    Guid DebitOrderId,
    string OriginalTransactionId,
    string CreditorName,
    string MandateType,
    string MandateReference,
    decimal Amount,
    string Currency,
    DateOnly ActionDate,
    string ReasonCode,
    string ReasonDescription,
    string? Comment,
    DisputeStatus Status,
    DisputeState State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    string? OutcomeNote,
    IReadOnlyList<DisputeEventDto> Events);

/// <summary>Query string for the dispute history list.</summary>
internal sealed class DisputeListQuery
{
    [FromQuery(Name = "state")]
    public DisputeState? State { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

internal static class DisputeMapper
{
    public static DisputeState StateOf(Dispute dispute) => dispute.IsCompleted ? DisputeState.Completed : DisputeState.Pending;

    public static DisputeSummaryDto ToSummary(Dispute dispute) => new(
        dispute.Id,
        dispute.Reference,
        dispute.DebitOrderId,
        dispute.CreditorName,
        dispute.Amount,
        dispute.Currency,
        dispute.ActionDate,
        dispute.ReasonCode,
        DescribeReason(dispute.ReasonCode),
        dispute.Status,
        StateOf(dispute),
        dispute.SubmittedAt,
        dispute.CompletedAt);

    public static DisputeDetailDto ToDetail(Dispute dispute) => new(
        dispute.Id,
        dispute.Reference,
        dispute.DebitOrderId,
        dispute.OriginalTransactionId,
        dispute.CreditorName,
        dispute.MandateType,
        dispute.MandateReference,
        dispute.Amount,
        dispute.Currency,
        dispute.ActionDate,
        dispute.ReasonCode,
        DescribeReason(dispute.ReasonCode),
        dispute.Comment,
        dispute.Status,
        StateOf(dispute),
        dispute.SubmittedAt,
        dispute.CompletedAt,
        dispute.OutcomeNote,
        dispute.Events
            .OrderBy(e => e.OccurredAt)
            .Select(e => new DisputeEventDto(e.Status, e.Description, e.OccurredAt))
            .ToList());

    private static string DescribeReason(string code) => DisputeReasons.Find(code)?.Description ?? code;
}
