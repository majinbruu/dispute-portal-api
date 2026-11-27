using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Transactions.Infrastructure;

internal sealed class EfDebitOrderStore(TransactionsDbContext db) : IDebitOrderStore
{
    public async Task<PagedResult<DebitOrder>> ListAsync(Guid customerId, DebitOrderFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = db.DebitOrders.AsNoTracking().Where(d => d.CustomerId == customerId);

        if (filter.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        if (filter.MandateType is { } mandateType)
        {
            query = query.Where(d => d.MandateType == mandateType);
        }

        if (filter.From is { } from)
        {
            query = query.Where(d => d.EffectiveDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(d => d.EffectiveDate <= to);
        }

        // The ID is always the last sort key so paging is stable when dates or amounts repeat.
        query = filter.Sort switch
        {
            DebitOrderSort.ActionDateAscending => query.OrderBy(d => d.EffectiveDate).ThenBy(d => d.Id),
            DebitOrderSort.AmountDescending => query.OrderByDescending(d => d.Amount).ThenBy(d => d.Id),
            DebitOrderSort.AmountAscending => query.OrderBy(d => d.Amount).ThenBy(d => d.Id),
            _ => query.OrderByDescending(d => d.EffectiveDate).ThenBy(d => d.Id),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<DebitOrder>(items, page.Page, page.PageSize, total);
    }

    public Task<DebitOrder?> FindAsync(Guid debitOrderId, Guid customerId, CancellationToken cancellationToken) =>
        db.DebitOrders.AsNoTracking().FirstOrDefaultAsync(d => d.Id == debitOrderId && d.CustomerId == customerId, cancellationToken);

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
}
