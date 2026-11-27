using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Application;

/// <summary>Read use cases for debit orders and mandates.</summary>
internal sealed class DebitOrderService(ITransactionStore store, IDisputeStatusProvider disputes, TimeProvider timeProvider)
{
    public async Task<PagedResult<DebitOrderDto>> ListAsync(Guid customerId, DebitOrderListQuery query, CancellationToken cancellationToken)
    {
        var filter = new DebitOrderFilter(query.Status, query.MandateType, query.From, query.To, DebitOrderSorts.Parse(query.Sort));
        var page = await store.ListAsync(customerId, filter, new PageRequest(query.Page, query.PageSize), cancellationToken);

        var disputeStatuses = await disputes.GetForTransactionsAsync(page.Items.Select(d => d.Id).ToList(), cancellationToken);
        var today = SouthAfricanTime.Today(timeProvider);

        return page.Map(debitOrder => DebitOrderMapper.ToDto(debitOrder, today, disputeStatuses.GetValueOrDefault(debitOrder.Id)));
    }

    public async Task<Result<DebitOrderDto>> GetAsync(Guid customerId, Guid debitOrderId, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(debitOrderId, customerId, cancellationToken) is not DebitOrder debitOrder)
        {
            return DebitOrderErrors.DebitOrderNotFound;
        }

        var disputeStatuses = await disputes.GetForTransactionsAsync([debitOrder.Id], cancellationToken);
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

internal static class DebitOrderErrors
{
    public static readonly Error DebitOrderNotFound = Error.NotFound("debit_order_not_found", "The debit order was not found.");

    public static readonly Error MandateNotFound = Error.NotFound("mandate_not_found", "The mandate was not found.");
}
