using DisputePortal.Modules.Transactions.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Transactions.Controllers;

[ApiController]
[Route("api/v1/mandates")]
[Authorize(Policy = ApiConventions.CustomerPolicy)]
[Tags("Mandates")]
internal sealed class MandatesController(TransactionService service, ICurrentCustomer customer) : ControllerBase
{
    [HttpGet(Name = "ListMandates")]
    [EndpointSummary("Lists the customer's debit order mandates, optionally by type.")]
    [ProducesResponseType<PagedResult<MandateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MandateDto>>> List([FromQuery] MandateListQuery query, CancellationToken cancellationToken) =>
        Ok(await service.ListMandatesAsync(customer.CustomerId, query, cancellationToken));

    [HttpGet("{id:guid}", Name = "GetMandate")]
    [EndpointSummary("Returns one mandate.")]
    [ProducesResponseType<MandateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MandateDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetMandateAsync(customer.CustomerId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
