using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Transactions.Infrastructure;

internal sealed class EfTransactionStore(TransactionsDbContext db) : ITransactionStore
{
    public Task<PagedResult<Transaction>> ListAsync(Guid customerId, TransactionFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        // Querying the set of one type joins the shared table to that type's table only.
        if (filter.Type == TransactionType.Card)
        {
            var cards = db.CardTransactions.AsNoTracking().Where(c => c.CustomerId == customerId);
            if (filter.CardStatus is { } cardStatus)
            {
                cards = cards.Where(c => c.Status == cardStatus);
            }

            return PageAsync(cards, filter, page, cancellationToken);
        }

        var debitOrders = db.DebitOrders.AsNoTracking().Where(d => d.CustomerId == customerId);
        if (filter.MandateType is { } mandateType)
        {
            debitOrders = debitOrders.Where(d => d.MandateType == mandateType);
        }

        if (filter.DebitOrderStatus is { } debitOrderStatus)
        {
            debitOrders = debitOrders.Where(d => d.Status == debitOrderStatus);
        }

        return PageAsync(debitOrders, filter, page, cancellationToken);
    }

    public Task<Transaction?> FindAsync(Guid transactionId, Guid customerId, CancellationToken cancellationToken) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId && t.CustomerId == customerId, cancellationToken);

    public async Task<PagedResult<Mandate>> ListMandatesAsync(Guid customerId, MandateType? type, PageRequest page, CancellationToken cancellationToken)
    {
        var query = db.Mandates.AsNoTracking().Where(m => m.CustomerId == customerId);
        if (type is { } mandateType)
        {
            query = query.Where(m => m.Type == mandateType);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(m => m.CreditorName).ThenBy(m => m.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Mandate>(items, page.Page, page.PageSize, total);
    }

    public Task<Mandate?> FindMandateAsync(Guid mandateId, Guid customerId, CancellationToken cancellationToken) =>
        db.Mandates.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mandateId && m.CustomerId == customerId, cancellationToken);

    /// <summary>The date range, sorting and paging are the same for every type, because they only use the shared columns.</summary>
    private static async Task<PagedResult<Transaction>> PageAsync<T>(
        IQueryable<T> query, TransactionFilter filter, PageRequest page, CancellationToken cancellationToken)
        where T : Transaction
    {
        if (filter.From is { } from)
        {
            query = query.Where(t => t.EffectiveDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(t => t.EffectiveDate <= to);
        }

        // The ID is always the last sort key so paging is stable when dates or amounts repeat.
        query = filter.Sort switch
        {
            TransactionSort.EffectiveDateAscending => query.OrderBy(t => t.EffectiveDate).ThenBy(t => t.Id),
            TransactionSort.AmountDescending => query.OrderByDescending(t => t.Amount).ThenBy(t => t.Id),
            TransactionSort.AmountAscending => query.OrderBy(t => t.Amount).ThenBy(t => t.Id),
            _ => query.OrderByDescending(t => t.EffectiveDate).ThenBy(t => t.Id),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<Transaction>(items, page.Page, page.PageSize, total);
    }
}
