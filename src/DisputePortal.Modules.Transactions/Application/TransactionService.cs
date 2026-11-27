using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Application;

/// <summary>Read use cases for transactions and mandates.</summary>
internal sealed class TransactionService(ITransactionStore store, IDisputeStatusProvider disputes, TimeProvider timeProvider)
{
    public async Task<PagedResult<TransactionDto>> ListAsync(Guid customerId, TransactionListQuery query, CancellationToken cancellationToken)
    {
        var filter = new TransactionFilter(
            query.Type, query.MandateType, query.DebitOrderStatus, query.CardStatus, query.From, query.To, TransactionSorts.Parse(query.Sort));
        var page = await store.ListAsync(customerId, filter, new PageRequest(query.Page, query.PageSize), cancellationToken);

        var disputeStatuses = await disputes.GetForTransactionsAsync(page.Items.Select(t => t.Id).ToList(), cancellationToken);
        var today = SouthAfricanTime.Today(timeProvider);

        return page.Map(transaction => TransactionMapper.ToDto(transaction, today, disputeStatuses.GetValueOrDefault(transaction.Id)));
    }

    public async Task<Result<TransactionDto>> GetAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await store.FindAsync(transactionId, customerId, cancellationToken);
        if (transaction is null)
        {
            return TransactionErrors.TransactionNotFound;
        }

        var disputeStatuses = await disputes.GetForTransactionsAsync([transaction.Id], cancellationToken);
        return Result<TransactionDto>.Success(
            TransactionMapper.ToDto(transaction, SouthAfricanTime.Today(timeProvider), disputeStatuses.GetValueOrDefault(transaction.Id)));
    }

    public async Task<PagedResult<MandateDto>> ListMandatesAsync(Guid customerId, MandateListQuery query, CancellationToken cancellationToken)
    {
        var page = await store.ListMandatesAsync(customerId, query.Type, new PageRequest(query.Page, query.PageSize), cancellationToken);
        return page.Map(TransactionMapper.ToDto);
    }

    public async Task<Result<MandateDto>> GetMandateAsync(Guid customerId, Guid mandateId, CancellationToken cancellationToken)
    {
        var mandate = await store.FindMandateAsync(mandateId, customerId, cancellationToken);
        return mandate is null ? TransactionErrors.MandateNotFound : Result<MandateDto>.Success(TransactionMapper.ToDto(mandate));
    }
}

internal static class TransactionErrors
{
    public static readonly Error TransactionNotFound = Error.NotFound("transaction_not_found", "The transaction was not found.");

    public static readonly Error MandateNotFound = Error.NotFound("mandate_not_found", "The mandate was not found.");
}
