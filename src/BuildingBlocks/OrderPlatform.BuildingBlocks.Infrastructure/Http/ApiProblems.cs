using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Problem details with stable error codes (RFC 9457, ADR-0020). Clients rely on <c>type</c> and <c>errorCode</c>;
/// <c>title</c> and <c>detail</c> may change.
/// </summary>
public static class ApiProblems
{
    public const string ErrorCodeExtension = "errorCode";
    public const string TraceIdExtension = "traceId";

    /// <summary>Base of the stable problem <c>type</c> URIs; the error code is appended.</summary>
    public const string TypeBaseUri = "https://problems.orderplatform.example/";

    public static string TypeFor(string errorCode) => TypeBaseUri + errorCode;

    /// <summary>Maps an expected failure returned by a handler to its HTTP status and problem details.</summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        var (status, title) = error.Kind switch
        {
            ErrorKind.Validation => (StatusCodes.Status400BadRequest, "The request is invalid."),
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "The resource was not found."),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "The request conflicts with the current state."),
            ErrorKind.BusinessRule => (StatusCodes.Status422UnprocessableEntity, "The request violates a business rule."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        return Problem(status, error.Code, title, error.Message, error.Details);
    }

    public static ProblemHttpResult Problem(
        int status,
        string errorCode,
        string title,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        // The failure's own fields first, so a module can never overwrite errorCode (ADR-0020).
        var extensions = new Dictionary<string, object?>(details ?? new Dictionary<string, object?>(), StringComparer.Ordinal)
        {
            [ErrorCodeExtension] = errorCode,
        };

        return TypedResults.Problem(statusCode: status, type: TypeFor(errorCode), title: title, detail: detail, extensions: extensions);
    }

    /// <summary>Field-level validation errors (<c>400</c>, error code <c>validation-failed</c>).</summary>
    public static ValidationProblem Validation(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(
            errors,
            type: TypeFor(ErrorCodes.ValidationFailed),
            title: "One or more fields are invalid.",
            extensions: new Dictionary<string, object?> { [ErrorCodeExtension] = ErrorCodes.ValidationFailed });
}

/// <summary>Error codes of the HTTP layer itself; business error codes are defined by the modules.</summary>
public static class ErrorCodes
{
    public const string BadRequest = "bad-request";
    public const string ValidationFailed = "validation-failed";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string MethodNotAllowed = "method-not-allowed";
    public const string Conflict = "conflict";
    public const string UnsupportedMediaType = "unsupported-media-type";
    public const string UnprocessableRequest = "unprocessable-request";
    public const string RateLimited = "rate-limited";
    public const string InternalError = "internal-error";

    public const string IdempotencyKeyRequired = "idempotency-key-required";
    public const string IdempotencyKeyInProgress = "idempotency-key-in-progress";
    public const string IdempotencyKeyReused = "idempotency-key-reused";

    /// <summary>Default code for responses produced by the framework (authentication, routing, model binding, exceptions).</summary>
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status422UnprocessableEntity => UnprocessableRequest,
        StatusCodes.Status429TooManyRequests => RateLimited,
        >= 500 => InternalError,
        _ => BadRequest,
    };
}
