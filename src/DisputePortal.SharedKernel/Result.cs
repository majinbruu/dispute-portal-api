namespace DisputePortal.SharedKernel;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unprocessable,
}

/// <summary>An expected business failure. <see cref="Code"/> is stable and safe for clients to branch on.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unprocessable(string code, string message) => new(code, message, ErrorType.Unprocessable);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
}

/// <summary>Outcome of a use case: a value, or an expected <see cref="Error"/>. Exceptions stay reserved for bugs.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public bool IsSuccess => Error is null;

    public Error? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result has no value because it failed with '{Error!.Code}'.");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
