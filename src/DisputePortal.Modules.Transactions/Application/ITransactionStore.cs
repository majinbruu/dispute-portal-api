using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Application;

/// <summary>Persistence port. Every read is scoped to a customer, so a query cannot forget the ownership filter.</summary>
internal interface ITransactionStore
{
    /// <summary>Lists transactions of the one type the filter names, each loaded with the details of that type.</summary>
    Task<PagedResult<Transaction>> ListAsync(Guid customerId, TransactionFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<Transaction?> FindAsync(Guid transactionId, Guid customerId, CancellationToken cancellationToken);

    Task<PagedResult<Mandate>> ListMandatesAsync(Guid customerId, MandateType? type, PageRequest page, CancellationToken cancellationToken);

    Task<Mandate?> FindMandateAsync(Guid mandateId, Guid customerId, CancellationToken cancellationToken);
}
