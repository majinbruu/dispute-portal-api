using DisputePortal.Modules.Transactions.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Transactions.Controllers;

[ApiController]
[Route("api/v1/transactions")]
[Authorize(Policy = ApiConventions.CustomerPolicy)]
[Tags("Transactions")]
internal sealed class TransactionsController(TransactionService service, ICurrentCustomer customer) : ControllerBase
{
    [HttpGet(Name = "ListTransactions")]
    [EndpointSummary("Lists the customer's transactions of one type, newest first, with whether each can be disputed.")]
    [ProducesResponseType<PagedResult<TransactionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<TransactionDto>>> List([FromQuery] TransactionListQuery query, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(customer.CustomerId, query, cancellationToken));

    [HttpGet("{id:guid}", Name = "GetTransaction")]
    [EndpointSummary("Returns one transaction with the details of its type and its dispute eligibility.")]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(customer.CustomerId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
