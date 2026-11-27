using DisputePortal.Modules.Disputes.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Modules.Disputes.Controllers;

/// <summary>
/// The agents' side of a dispute. Everything here needs the agent role, and nothing is scoped to a customer.
/// The agent's identity comes from the token and is recorded on each decision.
/// </summary>
[ApiController]
[Route("api/v1/agent/disputes")]
[Authorize(Policy = ApiConventions.AgentPolicy)]
[Tags("Agent")]
internal sealed class AgentDisputesController(
    AgentDisputeQueries queries,
    ReviewDisputeHandler review,
    AddCommentHandler addComment,
    ICurrentAgent agent) : ControllerBase
{
    [HttpGet(Name = "ListDisputesForAgent")]
    [EndpointSummary("Lists disputes across all customers. By default the review queue: disputes waiting for review first, oldest first.")]
    [ProducesResponseType<PagedResult<AgentDisputeSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AgentDisputeSummaryDto>>> List([FromQuery] AgentDisputeListQuery query, CancellationToken cancellationToken) =>
        Ok(await queries.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}", Name = "GetDisputeForAgent")]
    [EndpointSummary("Returns one dispute with the disputed transaction, the audit trail of who did what, and the comment thread.")]
    [ProducesResponseType<AgentDisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentDisputeDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await queries.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{id:guid}/approve", Name = "ApproveDispute")]
    [EndpointSummary("Approves a dispute that waits for review, which sends it for processing. Returns 409 if it was already reviewed or withdrawn.")]
    [ProducesResponseType<AgentDisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentDisputeDetailDto>> Approve(Guid id, CancellationToken cancellationToken)
    {
        var result = await review.ApproveAsync(agent.AgentId, id, cancellationToken);
        return result.IsSuccess ? Ok(AgentDisputeMapper.ToDetail(result.Value)) : this.ToProblem(result.Error!);
    }

    [HttpPost("{id:guid}/reject", Name = "RejectDispute")]
    [EndpointSummary("Rejects a dispute that waits for review. The reason is shown to the customer as the outcome. Returns 409 if it was already reviewed or withdrawn.")]
    [ProducesResponseType<AgentDisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentDisputeDetailDto>> Reject(Guid id, [FromBody] RejectDisputeRequest request, CancellationToken cancellationToken)
    {
        var result = await review.RejectAsync(agent.AgentId, id, request.Reason, cancellationToken);
        return result.IsSuccess ? Ok(AgentDisputeMapper.ToDetail(result.Value)) : this.ToProblem(result.Error!);
    }

    [HttpPost("{id:guid}/comments", Name = "AddDisputeCommentAsAgent")]
    [EnableRateLimiting(ApiConventions.DisputeCommentRateLimit)]
    [EndpointSummary("Adds an agent's comment to a dispute while it is open. The customer is notified in their inbox. Returns 409 once the dispute is closed.")]
    [ProducesResponseType<AgentDisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AgentDisputeDetailDto>> AddComment(Guid id, [FromBody] AddCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await addComment.ByAgentAsync(agent.AgentId, id, request.Body, cancellationToken);
        return result.IsSuccess ? Ok(AgentDisputeMapper.ToDetail(result.Value)) : this.ToProblem(result.Error!);
    }
}
