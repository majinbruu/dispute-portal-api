using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using FluentValidation;

namespace DisputePortal.Modules.Transactions.Application;

internal sealed class TransactionListQueryValidator : AbstractValidator<TransactionListQuery>
{
    public TransactionListQueryValidator()
    {
        RuleFor(q => q.Type).IsInEnum();

        // DebiCheck, Registered Mandate and EFT debit orders are never shown in one list.
        RuleFor(q => q.MandateType)
            .NotNull()
            .When(q => q.Type == TransactionType.DebitOrder)
            .WithMessage("'mandateType' is required for debit orders: DebiCheck, Registered or Eft.");
        RuleFor(q => q.CardStatus)
            .Null()
            .When(q => q.Type == TransactionType.DebitOrder)
            .WithMessage("'cardStatus' only applies to card transactions.");

        RuleFor(q => q.MandateType)
            .Null()
            .When(q => q.Type == TransactionType.Card)
            .WithMessage("'mandateType' only applies to debit orders.");
        RuleFor(q => q.DebitOrderStatus)
            .Null()
            .When(q => q.Type == TransactionType.Card)
            .WithMessage("'debitOrderStatus' only applies to debit orders.");

        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Sort)
            .Must(sort => TransactionSorts.ByName.ContainsKey(sort!))
            .When(q => q.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", TransactionSorts.ByName.Keys)}.");
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
