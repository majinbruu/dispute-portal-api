using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using FluentValidation;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>Read use cases for the dispute history.</summary>
internal sealed class DisputeQueries(IDisputeRepository disputes)
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

    public static IReadOnlyList<DisputeReasonDto> ReasonsFor(MandateKind? mandateType)
    {
        var reasons = mandateType is null ? DisputeReasons.All : DisputeReasons.AllowedFor(mandateType.Value.ToString());
        return reasons.Select(reason => new DisputeReasonDto(reason.Code, reason.Description)).ToList();
    }
}

internal sealed class SubmitDisputeRequestValidator : AbstractValidator<SubmitDisputeRequest>
{
    public SubmitDisputeRequestValidator()
    {
        RuleFor(r => r.DebitOrderId).NotEmpty();
        RuleFor(r => r.ReasonCode)
            .NotEmpty()
            .Must(code => DisputeReasons.Find(code) is not null)
            .WithMessage($"Reason code must be one of: {string.Join(", ", DisputeReasons.All.Select(r => r.Code))}.");
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
