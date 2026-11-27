using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using FluentValidation;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>Read use cases for agents. They are not scoped to a customer: an agent works across all of them.</summary>
internal sealed class AgentDisputeQueries(IDisputeRepository disputes)
{
    public async Task<PagedResult<AgentDisputeSummaryDto>> ListAsync(AgentDisputeListQuery query, CancellationToken cancellationToken)
    {
        var page = await disputes.ListForAgentAsync(
            AgentDisputeMapper.ToFilter(query), new PageRequest(query.Page, query.PageSize), cancellationToken);
        return page.Map(AgentDisputeMapper.ToSummary);
    }

    public async Task<Result<AgentDisputeDetailDto>> GetAsync(Guid disputeId, CancellationToken cancellationToken)
    {
        var dispute = await disputes.FindForAgentAsync(disputeId, cancellationToken);
        return dispute is null ? DisputeErrors.DisputeNotFound : Result<AgentDisputeDetailDto>.Success(AgentDisputeMapper.ToDetail(dispute));
    }
}

internal sealed class AgentDisputeListQueryValidator : AbstractValidator<AgentDisputeListQuery>
{
    public const int MaxReferenceLength = 30;

    public AgentDisputeListQueryValidator()
    {
        RuleFor(q => q.Reference).MaximumLength(MaxReferenceLength);
        RuleFor(q => q.Sort)
            .Must(sort => AgentDisputeSorts.ByName.ContainsKey(sort!))
            .When(q => q.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", AgentDisputeSorts.ByName.Keys)}.");
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(q => q.PageSize.HasValue);
    }
}

internal sealed class RejectDisputeRequestValidator : AbstractValidator<RejectDisputeRequest>
{
    public RejectDisputeRequestValidator()
    {
        // The reason is trimmed before it is stored, so the limits apply to what is left.
        RuleFor(r => r.Reason)
            .Must(reason => reason?.Trim().Length is >= 1 and <= Dispute.MaxRejectionReasonLength)
            .WithMessage($"A reason is required, of at most {Dispute.MaxRejectionReasonLength} characters.");
    }
}
