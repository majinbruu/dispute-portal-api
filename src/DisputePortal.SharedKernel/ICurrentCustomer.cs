namespace DisputePortal.SharedKernel;

/// <summary>
/// The signed-in customer. The identifier always comes from the validated access token,
/// never from a route, query string or body, so one customer cannot ask for another's data.
/// </summary>
public interface ICurrentCustomer
{
    Guid CustomerId { get; }
}
