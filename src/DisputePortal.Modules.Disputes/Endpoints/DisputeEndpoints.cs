using DisputePortal.Modules.Disputes.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DisputePortal.Modules.Disputes.Endpoints;

internal static class DisputeEndpoints
{
    private const int MaxIdempotencyKeyLength = 100;

    public static void Map(IEndpointRouteBuilder app)
    {
        var disputes = app.MapGroup("/api/v1/disputes")
            .WithTags("Disputes")
            .RequireAuthorization(ApiConventions.CustomerPolicy);

        disputes.MapPost("/", SubmitAsync)
            .AddEndpointFilter<ValidationFilter<SubmitDisputeRequest>>()
            .RequireRateLimiting(ApiConventions.DisputeSubmissionRateLimit)
            .WithName("SubmitDispute")
            .WithSummary("Raises a dispute against a debit order. Requires an Idempotency-Key header, which makes retries safe.")
            .Produces<DisputeDetailDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        disputes.MapGet("/", async ([AsParameters] DisputeListQuery query, DisputeQueries queries, ICurrentCustomer customer, CancellationToken cancellationToken) =>
                Results.Ok(await queries.ListAsync(customer.CustomerId, query, cancellationToken)))
            .AddEndpointFilter<ValidationFilter<DisputeListQuery>>()
            .WithName("ListDisputes")
            .WithSummary("Lists the customer's disputes, newest first. Filter by state to get pending or completed disputes.")
            .Produces<PagedResult<DisputeSummaryDto>>()
            .ProducesValidationProblem();

        disputes.MapGet("/{id:guid}", async (Guid id, DisputeQueries queries, ICurrentCustomer customer, CancellationToken cancellationToken) =>
            {
                var result = await queries.GetAsync(customer.CustomerId, id, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .WithName("GetDispute")
            .WithSummary("Returns one dispute with its progress timeline.")
            .Produces<DisputeDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/dispute-reasons", (MandateKind? mandateType) => Results.Ok(DisputeQueries.ReasonsFor(mandateType)))
            .WithTags("Disputes")
            .RequireAuthorization(ApiConventions.CustomerPolicy)
            .WithName("ListDisputeReasons")
            .WithSummary("Lists the dispute reasons a customer can choose, optionally only those allowed for a mandate type.")
            .Produces<IReadOnlyList<DisputeReasonDto>>();
    }

    private static async Task<IResult> SubmitAsync(
        SubmitDisputeRequest request,
        [FromHeader(Name = ApiConventions.IdempotencyKeyHeader)] string? idempotencyKey,
        SubmitDisputeHandler handler,
        ICurrentCustomer customer,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            return ProblemResults.Validation(new Dictionary<string, string[]>
            {
                [ApiConventions.IdempotencyKeyHeader] = [$"The {ApiConventions.IdempotencyKeyHeader} header is required and must be at most {MaxIdempotencyKeyLength} characters."],
            });
        }

        var result = await handler.HandleAsync(
            new SubmitDisputeCommand(customer.CustomerId, request.DebitOrderId, request.ReasonCode, request.Comment, idempotencyKey.Trim()),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        if (result.Value.Replayed)
        {
            httpContext.Response.Headers["Idempotent-Replayed"] = "true";
        }

        var dto = DisputeMapper.ToDetail(result.Value.Dispute);
        return Results.Created($"/api/v1/disputes/{dto.Id}", dto);
    }
}
