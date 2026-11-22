using DisputePortal.SharedKernel;
using FluentValidation;

namespace DisputePortal.Modules.DebitOrders.Application;

internal sealed class DebitOrderListQueryValidator : AbstractValidator<DebitOrderListQuery>
{
    public DebitOrderListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Sort)
            .Must(sort => DebitOrderSorts.ByName.ContainsKey(sort!))
            .When(q => q.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", DebitOrderSorts.ByName.Keys)}.");
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .When(q => q.From.HasValue && q.To.HasValue)
            .WithMessage("'to' must be on or after 'from'.");
    }
}

internal sealed class MandateListQueryValidator : AbstractValidator<MandateListQuery>
{
    public MandateListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(q => q.PageSize.HasValue);
    }
}
