using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;
using FluentValidation;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>Read use cases for the dispute history and the reasons a customer can choose.</summary>
internal sealed class DisputeQueries(IDisputeRepository disputes, ITransactionDirectory transactions)
{
    public async Task<PagedResult<DisputeSummaryDto>> ListAsync(Guid customerId, DisputeListQuery query, CancellationToken cancellationToken)
    {
        var page = await disputes.ListAsync(customerId, query.State, new PageRequest(query.Page, query.PageSize), cancellationToken);
        return page.Map(DisputeMapper.ToSummary);
    }

    public async Task<Result<DisputeDetailDto>> GetAsync(Guid customerId, Guid disputeId, CancellationToken cancellationToken)
    {
        var dispute = await disputes.FindAsync(disputeId, customerId, cancellationToken);
        return dispute is null ? DisputeErrors.DisputeNotFound : Result<DisputeDetailDto>.Success(DisputeMapper.ToDetail(dispute));
    }

    /// <summary>The reasons offered for one of the customer's transactions, each with the scheme code it resolves to.</summary>
    public async Task<Result<IReadOnlyList<DisputeReasonDto>>> ReasonsForAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await transactions.FindAsync(transactionId, customerId, cancellationToken);
        if (transaction is null)
        {
            return DisputeErrors.TransactionNotFound;
        }

        return Result<IReadOnlyList<DisputeReasonDto>>.Success(
            DisputeReasons.OfferedFor(DisputeMapper.ToDisputedTransaction(transaction))
                .Select(reason => new DisputeReasonDto(reason.Id, reason.Label, reason.Code))
                .ToList());
    }
}

internal sealed class SubmitDisputeRequestValidator : AbstractValidator<SubmitDisputeRequest>
{
    public SubmitDisputeRequestValidator()
    {
        RuleFor(r => r.TransactionId).NotEmpty();

        // Whether the reason is offered for this particular transaction is decided when the dispute is raised.
        RuleFor(r => r.ReasonId)
            .NotEmpty()
            .Must(id => DisputeReasons.KnownIds.Contains(id))
            .WithMessage($"Reason id must be one of: {string.Join(", ", DisputeReasons.KnownIds)}.");

        // Optional. A blank comment counts as none, and the limit applies to the trimmed text that is stored.
        RuleFor(r => r.Comment)
            .Must(comment => string.IsNullOrWhiteSpace(comment) || comment.Trim().Length <= DisputeComment.MaxLength)
            .WithMessage($"A comment can be at most {DisputeComment.MaxLength} characters.");
    }
}

internal sealed class AddCommentRequestValidator : AbstractValidator<AddCommentRequest>
{
    public AddCommentRequestValidator()
    {
        // The comment is trimmed before it is stored, so the limits apply to what is left.
        RuleFor(r => r.Body)
            .Must(body => body?.Trim().Length is >= 1 and <= DisputeComment.MaxLength)
            .WithMessage($"A comment is required, of at most {DisputeComment.MaxLength} characters.");
    }
}

internal sealed class DisputeListQueryValidator : AbstractValidator<DisputeListQuery>
{
    public DisputeListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(q => q.PageSize.HasValue);
    }
}
