using DisputePortal.Modules.DebitOrders.Contracts;
using DisputePortal.Modules.DebitOrders.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.DebitOrders.Application;

/// <summary>Read use cases for debit orders and mandates.</summary>
internal sealed class DebitOrderService(IDebitOrderStore store, IDisputeStatusProvider disputes, TimeProvider timeProvider)
{
    public async Task<PagedResult<DebitOrderDto>> ListAsync(Guid customerId, DebitOrderListQuery query, CancellationToken cancellationToken)
    {
        var filter = new DebitOrderFilter(query.Status, query.MandateType, query.From, query.To, DebitOrderSorts.Parse(query.Sort));
        var page = await store.ListAsync(customerId, filter, new PageRequest(query.Page, query.PageSize), cancellationToken);

        var disputeStatuses = await disputes.GetForDebitOrdersAsync(page.Items.Select(d => d.Id).ToList(), cancellationToken);
        var today = SouthAfricanTime.Today(timeProvider);

        return page.Map(debitOrder => DebitOrderMapper.ToDto(debitOrder, today, disputeStatuses.GetValueOrDefault(debitOrder.Id)));
    }

    public async Task<Result<DebitOrderDto>> GetAsync(Guid customerId, Guid debitOrderId, CancellationToken cancellationToken)
    {
        var debitOrder = await store.FindAsync(debitOrderId, customerId, cancellationToken);
        if (debitOrder is null)
        {
            return DebitOrderErrors.DebitOrderNotFound;
        }

        var disputeStatuses = await disputes.GetForDebitOrdersAsync([debitOrder.Id], cancellationToken);
        return Result<DebitOrderDto>.Success(
            DebitOrderMapper.ToDto(debitOrder, SouthAfricanTime.Today(timeProvider), disputeStatuses.GetValueOrDefault(debitOrder.Id)));
    }

    public async Task<PagedResult<MandateDto>> ListMandatesAsync(Guid customerId, MandateListQuery query, CancellationToken cancellationToken)
    {
        var page = await store.ListMandatesAsync(customerId, query.Type, new PageRequest(query.Page, query.PageSize), cancellationToken);
        return page.Map(DebitOrderMapper.ToDto);
    }

    public async Task<Result<MandateDto>> GetMandateAsync(Guid customerId, Guid mandateId, CancellationToken cancellationToken)
    {
        var mandate = await store.FindMandateAsync(mandateId, customerId, cancellationToken);
        return mandate is null ? DebitOrderErrors.MandateNotFound : Result<MandateDto>.Success(DebitOrderMapper.ToDto(mandate));
    }
}

/// <summary>Implements the contract other modules use to look up a debit order before disputing it.</summary>
internal sealed class DebitOrderDirectory(IDebitOrderStore store, TimeProvider timeProvider) : IDebitOrderDirectory
{
    public async Task<DebitOrderForDispute?> FindAsync(Guid debitOrderId, Guid customerId, CancellationToken cancellationToken)
    {
        var debitOrder = await store.FindAsync(debitOrderId, customerId, cancellationToken);
        if (debitOrder is null)
        {
            return null;
        }

        // Whether a dispute already exists is the Disputes module's knowledge, so it is not evaluated here.
        var eligibility = debitOrder.EvaluateEligibility(SouthAfricanTime.Today(timeProvider), alreadyDisputed: false);
        var eligible = eligibility == DisputeEligibility.Eligible;

        return new DebitOrderForDispute(
            debitOrder.Id,
            debitOrder.CustomerId,
            debitOrder.TransactionId,
            debitOrder.EndToEndId,
            debitOrder.Amount,
            debitOrder.Currency,
            debitOrder.ActionDate,
            debitOrder.MandateType.ToString(),
            debitOrder.MandateReference,
            debitOrder.CreditorName,
            debitOrder.DebtorAccountMasked,
            eligible ? null : ToErrorCode(eligibility),
            eligible ? null : EligibilityMessages.For(eligibility, debitOrder));
    }

    private static string ToErrorCode(DisputeEligibility eligibility) => eligibility switch
    {
        DisputeEligibility.WithinMandateTerms => "within_mandate_terms",
        DisputeEligibility.CollectionRejected => "collection_rejected",
        DisputeEligibility.DisputeWindowClosed => "dispute_window_closed",
        _ => "not_disputable",
    };
}

internal static class DebitOrderErrors
{
    public static readonly Error DebitOrderNotFound = Error.NotFound("debit_order_not_found", "The debit order was not found.");

    public static readonly Error MandateNotFound = Error.NotFound("mandate_not_found", "The mandate was not found.");
}
