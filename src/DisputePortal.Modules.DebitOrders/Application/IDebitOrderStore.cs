using DisputePortal.Modules.DebitOrders.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.DebitOrders.Application;

/// <summary>Persistence port. Every read is scoped to a customer, so a query cannot forget the ownership filter.</summary>
internal interface IDebitOrderStore
{
    Task<PagedResult<DebitOrder>> ListAsync(Guid customerId, DebitOrderFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<DebitOrder?> FindAsync(Guid debitOrderId, Guid customerId, CancellationToken cancellationToken);

    Task<PagedResult<Mandate>> ListMandatesAsync(Guid customerId, MandateType? type, PageRequest page, CancellationToken cancellationToken);

    Task<Mandate?> FindMandateAsync(Guid mandateId, Guid customerId, CancellationToken cancellationToken);
}
