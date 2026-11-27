using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>How the customer thinks about a dispute: still in progress, or finished (refunded, declined or withdrawn).</summary>
internal enum DisputeState
{
    Pending,
    Completed,
}

/// <param name="Comment">Optional. When given it becomes the first comment in the dispute's thread.</param>
internal sealed record SubmitDisputeRequest(Guid TransactionId, string ReasonId, string? Comment = null);

internal sealed record AddCommentRequest(string Body);

/// <summary>A reason the customer may choose for one transaction. The code is the scheme's code for that transaction.</summary>
internal sealed record DisputeReasonDto(string Id, string Label, string Code);

/// <param name="Actor">Who caused the event. The customer is told that an agent acted, never which agent.</param>
internal sealed record DisputeEventDto(DisputeStatus Status, string Description, DisputeActorType Actor, DateTimeOffset OccurredAt);

/// <summary>A comment as the customer sees it: who wrote it is a customer or an agent, never which agent.</summary>
internal sealed record DisputeCommentDto(Guid Id, DisputeActorType AuthorType, string Body, DateTimeOffset CreatedAt);

internal sealed record DisputeSummaryDto(
    Guid Id,
    string Reference,
    Guid TransactionId,
    DisputedTransactionType TransactionType,
    string CounterpartyName,
    decimal Amount,
    string Currency,
    DateOnly TransactionDate,
    string ReasonId,
    string ReasonCode,
    string ReasonLabel,
    DisputeStatus Status,
    DisputeState State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt);

internal sealed record DisputeDetailDto(
    Guid Id,
    string Reference,
    Guid TransactionId,
    DisputedTransactionType TransactionType,
    string SchemeReference,
    string CounterpartyName,
    string? MandateType,
    string? MandateReference,
    string? CardScheme,
    decimal Amount,
    string Currency,
    DateOnly TransactionDate,
    string ReasonId,
    string ReasonCode,
    string ReasonLabel,
    DisputeStatus Status,
    DisputeState State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    string? OutcomeNote,
    IReadOnlyList<DisputeEventDto> Events,
    IReadOnlyList<DisputeCommentDto> Comments);

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
        dispute.TransactionId,
        dispute.TransactionType,
        dispute.CounterpartyName,
        dispute.Amount,
        dispute.Currency,
        dispute.TransactionDate,
        dispute.ReasonId,
        dispute.ReasonCode,
        DisputeReasons.LabelOf(dispute.TransactionType, dispute.ReasonId),
        dispute.Status,
        StateOf(dispute),
        dispute.SubmittedAt,
        dispute.CompletedAt);

    public static DisputeDetailDto ToDetail(Dispute dispute) => new(
        dispute.Id,
        dispute.Reference,
        dispute.TransactionId,
        dispute.TransactionType,
        dispute.SchemeReference,
        dispute.CounterpartyName,
        dispute.MandateType,
        dispute.MandateReference,
        dispute.CardScheme,
        dispute.Amount,
        dispute.Currency,
        dispute.TransactionDate,
        dispute.ReasonId,
        dispute.ReasonCode,
        DisputeReasons.LabelOf(dispute.TransactionType, dispute.ReasonId),
        dispute.Status,
        StateOf(dispute),
        dispute.SubmittedAt,
        dispute.CompletedAt,
        dispute.OutcomeNote,
        dispute.Events
            .OrderBy(e => e.OccurredAt)
            .Select(e => new DisputeEventDto(e.Status, e.Description, e.ActorType, e.OccurredAt))
            .ToList(),
        dispute.Comments
            .OrderBy(c => c.CreatedAt)
            .Select(c => new DisputeCommentDto(c.Id, c.AuthorType, c.Body, c.CreatedAt))
            .ToList());

    /// <summary>Copies what a dispute needs from the Transactions module's contract into this module's own model.</summary>
    public static DisputedTransaction ToDisputedTransaction(TransactionForDispute transaction) => new(
        transaction.Id,
        Enum.Parse<DisputedTransactionType>(transaction.Type),
        transaction.SchemeReference,
        new Money(transaction.Amount, transaction.Currency),
        transaction.TransactionDate,
        transaction.CounterpartyName,
        transaction.AccountMasked,
        transaction.DebitOrder is { } debitOrder
            ? new DisputedDebitOrder(debitOrder.EndToEndId, debitOrder.MandateType, debitOrder.MandateReference)
            : null,
        transaction.Card is { } card
            ? new DisputedCard(card.Scheme, card.EntryMode, card.Verification, card.PostingDate)
            : null);
}
