using DisputePortal.Modules.Disputes.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Modules.Disputes.Controllers;

[ApiController]
[Route("api/v1/disputes")]
[Authorize(Policy = ApiConventions.CustomerPolicy)]
[Tags("Disputes")]
internal sealed class DisputesController(
    SubmitDisputeHandler submitDispute,
    WithdrawDisputeHandler withdrawDispute,
    DisputeQueries queries,
    ICurrentCustomer customer) : ControllerBase
{
    private const int MaxIdempotencyKeyLength = 100;

    [HttpPost(Name = "SubmitDispute")]
    [EnableRateLimiting(ApiConventions.DisputeSubmissionRateLimit)]
    [EndpointSummary("Raises a dispute against a debit order or a card transaction. Requires an Idempotency-Key header, which makes retries safe.")]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitDisputeRequest request,
        [FromHeader(Name = ApiConventions.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            return this.ValidationFailed(
                ApiConventions.IdempotencyKeyHeader,
                $"The {ApiConventions.IdempotencyKeyHeader} header is required and must be at most {MaxIdempotencyKeyLength} characters.");
        }

        var result = await submitDispute.HandleAsync(
            new SubmitDisputeCommand(customer.CustomerId, request.TransactionId, request.ReasonId, idempotencyKey.Trim()),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToProblem(result.Error!);
        }

        if (result.Value.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        var dispute = DisputeMapper.ToDetail(result.Value.Dispute);
        return Created($"/api/v1/disputes/{dispute.Id}", dispute);
    }

    [HttpGet(Name = "ListDisputes")]
    [EndpointSummary("Lists the customer's disputes, newest first. Filter by state to get pending or completed disputes.")]
    [ProducesResponseType<PagedResult<DisputeSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DisputeSummaryDto>>> List([FromQuery] DisputeListQuery query, CancellationToken cancellationToken) =>
        Ok(await queries.ListAsync(customer.CustomerId, query, cancellationToken));

    [HttpGet("{id:guid}", Name = "GetDispute")]
    [EndpointSummary("Returns one dispute with its progress timeline.")]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DisputeDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await queries.GetAsync(customer.CustomerId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{id:guid}/withdraw", Name = "WithdrawDispute")]
    [EndpointSummary("Withdraws the customer's own dispute while it still waits for review. Returns 409 once it has been reviewed.")]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeDetailDto>> Withdraw(Guid id, CancellationToken cancellationToken)
    {
        var result = await withdrawDispute.HandleAsync(customer.CustomerId, id, cancellationToken);
        return result.IsSuccess ? Ok(DisputeMapper.ToDetail(result.Value)) : this.ToProblem(result.Error!);
    }
}

[ApiController]
[Route("api/v1/dispute-reasons")]
[Authorize(Policy = ApiConventions.CustomerPolicy)]
[Tags("Disputes")]
internal sealed class DisputeReasonsController(DisputeQueries queries, ICurrentCustomer customer) : ControllerBase
{
    [HttpGet(Name = "ListDisputeReasons")]
    [EndpointSummary("Lists the dispute reasons the customer can choose for one of their transactions, each with the scheme's code for it.")]
    [ProducesResponseType<IReadOnlyList<DisputeReasonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DisputeReasonDto>>> List(
        [FromQuery(Name = "transactionId"), BindRequired] Guid transactionId, CancellationToken cancellationToken)
    {
        var result = await queries.ReasonsForAsync(customer.CustomerId, transactionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
