using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.Infrastructure.Http;
using OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;
using OrderPlatform.BuildingBlocks.Infrastructure.Security;
using Wolverine;

namespace OrderPlatform.Api.Diagnostics;

/// <summary>
/// Development-only endpoint verifying the API conventions before business endpoints exist (implementation plan, PR 1b):
/// authentication and scopes, account scoping, idempotency, problem details, JSON conventions and a feature flag.
/// Removed in Phase 2 once the Ordering endpoints cover the same behaviour.
/// </summary>
internal static class DiagnosticsEndpoints
{
    private const int MaxMessageLength = 200;

    public sealed record EchoRequest(string? Message, Money? Amount, Guid? AccountId, EchoSimulation? Simulate);

    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder version)
    {
        version.MapPost("/_diagnostics/echo", EchoAsync)
            .WithName("EchoDiagnostics")
            .WithTags("Diagnostics (Development only)")
            .RequireAuthorization(PlatformPolicies.CustomerAccount, PlatformScopes.OrdersWrite)
            .RequireIdempotencyKey()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return version;
    }

    /// <summary>Includes the diagnostics handler explicitly (ADR-0004: discovery never depends on naming alone).</summary>
    public static void AddDiagnosticsHandlers(this WolverineOptions options) =>
        options.Discovery.IncludeType(typeof(EchoDiagnosticsHandler));

    // The endpoint validates the request shape, maps it to a command, invokes it and maps the Result (ADR-0011).
    private static async Task<Results<Ok<EchoResponse>, ValidationProblem, ProblemHttpResult>> EchoAsync(
        EchoRequest request,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > MaxMessageLength)
        {
            errors["message"] = [$"A message of 1 to {MaxMessageLength} characters is required."];
        }

        if (request.Amount is { Currency: not Money.Eur })
        {
            errors["amount.currency"] = [$"Only {Money.Eur} is supported."];
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(errors);
        }

        var command = new EchoDiagnostics(
            user.GetRequiredAccountId(),
            request.Message!,
            request.Amount,
            request.AccountId,
            request.Simulate ?? EchoSimulation.None);

        var result = await bus.InvokeAsync<Result<EchoResponse>>(command, cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
