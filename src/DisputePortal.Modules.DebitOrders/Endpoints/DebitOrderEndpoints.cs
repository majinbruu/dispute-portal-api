using DisputePortal.Modules.DebitOrders.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DisputePortal.Modules.DebitOrders.Endpoints;

internal static class DebitOrderEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var debitOrders = app.MapGroup("/api/v1/debit-orders")
            .WithTags("Debit orders")
            .RequireAuthorization(ApiConventions.CustomerPolicy);

        debitOrders.MapGet("/", async ([AsParameters] DebitOrderListQuery query, DebitOrderService service, ICurrentCustomer customer, CancellationToken cancellationToken) =>
                Results.Ok(await service.ListAsync(customer.CustomerId, query, cancellationToken)))
            .AddEndpointFilter<ValidationFilter<DebitOrderListQuery>>()
            .WithName("ListDebitOrders")
            .WithSummary("Lists the customer's debit orders, newest first, with whether each can be disputed.")
            .Produces<PagedResult<DebitOrderDto>>()
            .ProducesValidationProblem();

        debitOrders.MapGet("/{id:guid}", async (Guid id, DebitOrderService service, ICurrentCustomer customer, CancellationToken cancellationToken) =>
            {
                var result = await service.GetAsync(customer.CustomerId, id, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .WithName("GetDebitOrder")
            .WithSummary("Returns one debit order with its dispute eligibility.")
            .Produces<DebitOrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var mandates = app.MapGroup("/api/v1/mandates")
            .WithTags("Mandates")
            .RequireAuthorization(ApiConventions.CustomerPolicy);

        mandates.MapGet("/", async ([AsParameters] MandateListQuery query, DebitOrderService service, ICurrentCustomer customer, CancellationToken cancellationToken) =>
                Results.Ok(await service.ListMandatesAsync(customer.CustomerId, query, cancellationToken)))
            .AddEndpointFilter<ValidationFilter<MandateListQuery>>()
            .WithName("ListMandates")
            .WithSummary("Lists the customer's debit order mandates, optionally by type.")
            .Produces<PagedResult<MandateDto>>()
            .ProducesValidationProblem();

        mandates.MapGet("/{id:guid}", async (Guid id, DebitOrderService service, ICurrentCustomer customer, CancellationToken cancellationToken) =>
            {
                var result = await service.GetMandateAsync(customer.CustomerId, id, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .WithName("GetMandate")
            .WithSummary("Returns one mandate.")
            .Produces<MandateDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
