using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputePortal.SharedKernel.Web;

/// <summary>Turns expected business errors into RFC 9457 problem details with a stable code and the trace ID.</summary>
public static class ProblemResults
{
    /// <summary>The problem response for an expected business error. The trace ID is added by the problem details factory.</summary>
    public static ObjectResult ToProblem(this ControllerBase controller, Error error)
    {
        var result = controller.Problem(detail: error.Message, statusCode: StatusCodeFor(error.Type), title: TitleFor(error.Type));
        if (result.Value is ProblemDetails problem)
        {
            problem.Extensions["code"] = error.Code;
        }

        return result;
    }

    /// <summary>A 400 response for one invalid field, in the same shape as every other validation failure.</summary>
    public static IActionResult ValidationFailed(this ControllerBase controller, string field, string message)
    {
        controller.ModelState.AddModelError(field, message);
        var apiBehavior = controller.HttpContext.RequestServices.GetRequiredService<IOptions<ApiBehaviorOptions>>();
        return apiBehavior.Value.InvalidModelStateResponseFactory(controller.ControllerContext);
    }

    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "The request is invalid.",
        ErrorType.NotFound => "The resource was not found.",
        ErrorType.Conflict => "The request conflicts with the current state.",
        ErrorType.Unprocessable => "The request cannot be processed.",
        ErrorType.Forbidden => "You are not allowed to do this.",
        _ => "An unexpected error occurred.",
    };
}
