using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace DisputePortal.SharedKernel.Web;

/// <summary>Turns expected business errors into RFC 9457 problem details with a stable code and the trace ID.</summary>
public static class ProblemResults
{
    public static IResult ToProblem(this Error error) => Results.Problem(
        statusCode: StatusCodeFor(error.Type),
        title: TitleFor(error.Type),
        detail: error.Message,
        extensions: Extensions(error.Code));

    public static IResult Validation(IDictionary<string, string[]> errors) => Results.ValidationProblem(
        errors,
        detail: "One or more fields are invalid.",
        extensions: Extensions("validation_failed"));

    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "The request is invalid.",
        ErrorType.NotFound => "The resource was not found.",
        ErrorType.Conflict => "The request conflicts with the current state.",
        ErrorType.Unprocessable => "The request cannot be processed.",
        _ => "An unexpected error occurred.",
    };

    private static Dictionary<string, object?> Extensions(string code) => new()
    {
        ["code"] = code,
        ["traceId"] = Activity.Current?.Id,
    };
}
