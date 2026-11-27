using DisputePortal.Modules.Disputes.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Disputes.Application;

internal sealed record RejectDisputeRequest(string Reason);

/// <summary>A dispute as it appears in the agents' queue, which spans all customers.</summary>
internal sealed record AgentDisputeSummaryDto(
    Guid Id,
    string Reference,
    Guid CustomerId,
    DisputedTransactionType TransactionType,
    string CounterpartyName,
    decimal Amount,
    string Currency,
    DateOnly TransactionDate,
    string ReasonId,
    string ReasonCode,
    string ReasonLabel,
    DisputeStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// The disputed transaction as it was when the dispute was raised. The dispute keeps its own copy,
/// so an agent sees what the customer disputed even if the transaction is corrected later.
/// </summary>
/// <param name="SchemeReference">The pacs.003 TxId of a debit order, or the acquirer reference number of a card transaction.</param>
/// <param name="MandateType">Debit orders only: DebiCheck, Registered or Eft.</param>
/// <param name="MandateReference">DebiCheck and Registered Mandate debit orders only.</param>
/// <param name="CardScheme">Card transactions only.</param>
/// <param name="PostingDate">Card transactions only.</param>
internal sealed record DisputedTransactionDto(
    Guid Id,
    DisputedTransactionType Type,
    string SchemeReference,
    string CounterpartyName,
    decimal Amount,
    string Currency,
    DateOnly TransactionDate,
    DateOnly? PostingDate,
    string AccountMasked,
    string? MandateType,
    string? MandateReference,
    string? CardScheme);

/// <summary>An entry in the audit trail. Unlike the customer's timeline it says exactly who acted.</summary>
/// <param name="ActorSubjectId">The token subject of the customer or agent. Null when the system acted.</param>
internal sealed record AgentDisputeEventDto(
    DisputeStatus Status,
    string Description,
    DisputeActorType Actor,
    Guid? ActorSubjectId,
    DateTimeOffset OccurredAt);

internal sealed record AgentDisputeDetailDto(
    Guid Id,
    string Reference,
    Guid CustomerId,
    DisputedTransactionDto Transaction,
    string ReasonId,
    string ReasonCode,
    string ReasonLabel,
    DisputeStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? SentToSchemeAt,
    DateTimeOffset? CompletedAt,
    string? OutcomeNote,
    IReadOnlyList<AgentDisputeEventDto> Events);

/// <summary>Query string for the agents' dispute queue.</summary>
internal sealed class AgentDisputeListQuery
{
    [FromQuery(Name = "status")]
    public DisputeStatus? Status { get; init; }

    [FromQuery(Name = "transactionType")]
    public DisputedTransactionType? TransactionType { get; init; }

    /// <summary>Part of a dispute reference, in any letter case.</summary>
    [FromQuery(Name = "reference")]
    public string? Reference { get; init; }

    /// <summary>
    /// One of: submittedAt, -submittedAt, amount, -amount. When left out the list is the review queue:
    /// disputes waiting for review first, and the oldest first.
    /// </summary>
    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

internal enum AgentDisputeSort
{
    /// <summary>Disputes waiting for review first, then the oldest first.</summary>
    ReviewQueue,
    SubmittedAtAscending,
    SubmittedAtDescending,
    AmountAscending,
    AmountDescending,
}

/// <param name="ReferenceContains">Upper case, because dispute references are.</param>
internal sealed record AgentDisputeFilter(
    DisputeStatus? Status,
    DisputedTransactionType? TransactionType,
    string? ReferenceContains,
    AgentDisputeSort Sort)
{
    /// <summary>
    /// Filters and orders disputes for the agents' list. Written against IQueryable so the database runs it,
    /// and so the unit tests run the very same rules in memory.
    /// </summary>
    public IQueryable<Dispute> Apply(IQueryable<Dispute> disputes)
    {
        if (Status is { } status)
        {
            disputes = disputes.Where(d => d.Status == status);
        }

        if (TransactionType is { } transactionType)
        {
            disputes = disputes.Where(d => d.TransactionType == transactionType);
        }

        if (ReferenceContains is { } reference)
        {
            disputes = disputes.Where(d => d.Reference.Contains(reference));
        }

        // The ID is always the last sort key so paging is stable when times or amounts repeat.
        return Sort switch
        {
            AgentDisputeSort.SubmittedAtAscending => disputes.OrderBy(d => d.SubmittedAt).ThenBy(d => d.Id),
            AgentDisputeSort.SubmittedAtDescending => disputes.OrderByDescending(d => d.SubmittedAt).ThenBy(d => d.Id),
            AgentDisputeSort.AmountAscending => disputes.OrderBy(d => d.Amount).ThenBy(d => d.Id),
            AgentDisputeSort.AmountDescending => disputes.OrderByDescending(d => d.Amount).ThenBy(d => d.Id),
            _ => disputes.OrderBy(d => d.Status == DisputeStatus.Submitted ? 0 : 1).ThenBy(d => d.SubmittedAt).ThenBy(d => d.Id),
        };
    }
}

internal static class AgentDisputeSorts
{
    public static readonly IReadOnlyDictionary<string, AgentDisputeSort> ByName = new Dictionary<string, AgentDisputeSort>(StringComparer.Ordinal)
    {
        ["submittedAt"] = AgentDisputeSort.SubmittedAtAscending,
        ["-submittedAt"] = AgentDisputeSort.SubmittedAtDescending,
        ["amount"] = AgentDisputeSort.AmountAscending,
        ["-amount"] = AgentDisputeSort.AmountDescending,
    };

    public static AgentDisputeSort Parse(string? value) =>
        value is not null && ByName.TryGetValue(value, out var sort) ? sort : AgentDisputeSort.ReviewQueue;
}

internal static class AgentDisputeMapper
{
    public static AgentDisputeFilter ToFilter(AgentDisputeListQuery query) => new(
        query.Status,
        query.TransactionType,
        string.IsNullOrWhiteSpace(query.Reference) ? null : query.Reference.Trim().ToUpperInvariant(),
        AgentDisputeSorts.Parse(query.Sort));

    public static AgentDisputeSummaryDto ToSummary(Dispute dispute) => new(
        dispute.Id,
        dispute.Reference,
        dispute.CustomerId,
        dispute.TransactionType,
        dispute.CounterpartyName,
        dispute.Amount,
        dispute.Currency,
        dispute.TransactionDate,
        dispute.ReasonId,
        dispute.ReasonCode,
        DisputeReasons.LabelOf(dispute.TransactionType, dispute.ReasonId),
        dispute.Status,
        dispute.SubmittedAt,
        dispute.CompletedAt);

    public static AgentDisputeDetailDto ToDetail(Dispute dispute) => new(
        dispute.Id,
        dispute.Reference,
        dispute.CustomerId,
        new DisputedTransactionDto(
            dispute.TransactionId,
            dispute.TransactionType,
            dispute.SchemeReference,
            dispute.CounterpartyName,
            dispute.Amount,
            dispute.Currency,
            dispute.TransactionDate,
            dispute.PostingDate,
            dispute.AccountMasked,
            dispute.MandateType,
            dispute.MandateReference,
            dispute.CardScheme),
        dispute.ReasonId,
        dispute.ReasonCode,
        DisputeReasons.LabelOf(dispute.TransactionType, dispute.ReasonId),
        dispute.Status,
        dispute.SubmittedAt,
        dispute.SentToSchemeAt,
        dispute.CompletedAt,
        dispute.OutcomeNote,
        dispute.Events
            .OrderBy(e => e.OccurredAt)
            .Select(e => new AgentDisputeEventDto(e.Status, e.Description, e.ActorType, e.ActorSubjectId, e.OccurredAt))
            .ToList());
}
