using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputePortal.Modules.Disputes.Infrastructure;

internal sealed class EfDisputeRepository(DisputesDbContext db) : IDisputeRepository
{
    public Task<IdempotencyRecord?> FindIdempotencyRecordAsync(Guid customerId, string key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.CustomerId == customerId && r.Key == key, cancellationToken);

    public Task<Dispute?> FindAsync(Guid disputeId, Guid customerId, CancellationToken cancellationToken) =>
        db.Disputes.AsNoTracking()
            .Include(d => d.Events)
            .FirstOrDefaultAsync(d => d.Id == disputeId && d.CustomerId == customerId, cancellationToken);

    public Task<bool> ExistsForTransactionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        db.Disputes.AnyAsync(d => d.TransactionId == transactionId && d.Status != DisputeStatus.Withdrawn, cancellationToken);

    public async Task<bool> TryAddAsync(Dispute dispute, IdempotencyRecord idempotencyRecord, CancellationToken cancellationToken)
    {
        db.Disputes.Add(dispute);
        db.IdempotencyRecords.Add(idempotencyRecord);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race on the transaction or the idempotency key. Nothing was written; the caller decides what to return.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<PagedResult<Dispute>> ListAsync(Guid customerId, DisputeState? state, PageRequest page, CancellationToken cancellationToken)
    {
        var query = db.Disputes.AsNoTracking().Where(d => d.CustomerId == customerId);

        query = state switch
        {
            DisputeState.Pending => query.Where(d => d.Status == DisputeStatus.Submitted || d.Status == DisputeStatus.SentToScheme),
            DisputeState.Completed => query.Where(d => d.Status != DisputeStatus.Submitted && d.Status != DisputeStatus.SentToScheme),
            _ => query,
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(d => d.SubmittedAt).ThenBy(d => d.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Dispute>(items, page.Page, page.PageSize, total);
    }
}

/// <summary>
/// Tells the Transactions module which transactions already have a dispute, through that module's own contract.
/// A withdrawn dispute is left out, so the transaction is shown as disputable again.
/// </summary>
internal sealed class DisputeStatusProvider(DisputesDbContext db) : IDisputeStatusProvider
{
    public async Task<IReadOnlyDictionary<Guid, TransactionDisputeStatus>> GetForTransactionsAsync(
        IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return new Dictionary<Guid, TransactionDisputeStatus>();
        }

        var rows = await db.Disputes.AsNoTracking()
            .Where(d => transactionIds.Contains(d.TransactionId) && d.Status != DisputeStatus.Withdrawn)
            .Select(d => new { d.TransactionId, d.Id, d.Reference, d.Status })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TransactionId, r => new TransactionDisputeStatus(r.Id, r.Reference, r.Status.ToString()));
    }
}
