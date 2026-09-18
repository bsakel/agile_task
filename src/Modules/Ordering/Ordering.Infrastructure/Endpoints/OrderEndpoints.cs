using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.Infrastructure.Http;
using OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;
using OrderPlatform.BuildingBlocks.Infrastructure.Security;
using OrderPlatform.Ordering.Application;
using Wolverine;

namespace OrderPlatform.Ordering.Infrastructure.Endpoints;

/// <summary>
/// The customer-facing order endpoints (ADR-0017 §3). Each one validates the request shape, maps it to a command,
/// invokes it and maps the <c>Result</c> — no business rule lives here (ADR-0011).
/// </summary>
internal static class OrderEndpoints
{
    private const int MaxLines = 100;
    private const int MaxQuantity = 10_000;

    public sealed record SubmitOrderRequest(IReadOnlyList<SubmitOrderRequestLine>? Lines);

    public sealed record SubmitOrderRequestLine(string? Sku, int Quantity);

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder version)
    {
        version.MapPost("/orders", SubmitAsync)
            .WithName("SubmitOrder")
            .WithTags("Orders")
            .RequireAuthorization(PlatformPolicies.CustomerAccount, PlatformScopes.OrdersWrite)
            .RequireIdempotencyKey();

        version.MapPost("/orders/{id:guid}/cancel", CancelAsync)
            .WithName("CancelOrder")
            .WithTags("Orders")
            .RequireAuthorization(PlatformPolicies.CustomerAccount, PlatformScopes.OrdersCancel)
            .RequireIdempotencyKey()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        version.MapGet("/orders/{id:guid}", GetAsync)
            .WithName("GetOrder")
            .WithTags("Orders")
            .RequireAuthorization(PlatformPolicies.CustomerAccount, PlatformScopes.OrdersRead)
            .ProducesProblem(StatusCodes.Status404NotFound);

        version.MapGet("/orders/{id:guid}/history", GetHistoryAsync)
            .WithName("GetOrderHistory")
            .WithTags("Orders")
            .RequireAuthorization(PlatformPolicies.CustomerAccount, PlatformScopes.OrdersRead)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return version;
    }

    /// <summary>
    /// Cancellation is accepted, not completed: the compensation runs through the outbox, so the order reaches
    /// <c>Cancelled</c> once every step has been undone (ADR-0017 §5, ADR-0020 long-running operations).
    /// </summary>
    private static async Task<Results<Accepted<CancelOrderResponse>, ProblemHttpResult>> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<CancelOrderResponse>>(
            new CancelOrder(user.GetRequiredAccountId(), id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Accepted($"/v1/orders/{id}", result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<OrderView>, ProblemHttpResult>> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<OrderView>>(new GetOrder(user.GetRequiredAccountId(), id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<OrderHistoryView>, ProblemHttpResult>> GetHistoryAsync(
        Guid id,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<OrderHistoryView>>(
            new GetOrderHistory(user.GetRequiredAccountId(), id), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<Results<Created<SubmitOrderResponse>, ValidationProblem, ProblemHttpResult>> SubmitAsync(
        SubmitOrderRequest request,
        ClaimsPrincipal user,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { Count: > 0 } errors)
        {
            return ApiProblems.Validation(errors);
        }

        var command = new SubmitOrder(
            user.GetRequiredAccountId(),
            [.. request.Lines!.Select(line => new SubmitOrderLine(line.Sku!, line.Quantity))]);

        var result = await bus.InvokeAsync<Result<SubmitOrderResponse>>(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/v1/orders/{result.Value.OrderId}", result.Value)
            : result.Error.ToProblem();
    }

    /// <summary>
    /// Shape only: that a SKU exists in the price list is a business rule Pricing answers with <c>unknown-product</c>
    /// (ADR-0018), not a validation error.
    /// </summary>
    private static Dictionary<string, string[]> Validate(SubmitOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Lines is not { Count: > 0 })
        {
            errors["lines"] = ["At least one order line is required."];
            return errors;
        }

        if (request.Lines.Count > MaxLines)
        {
            errors["lines"] = [$"An order has at most {MaxLines} lines."];
        }

        foreach (var (line, index) in request.Lines.Select((line, index) => (line, index)))
        {
            if (string.IsNullOrWhiteSpace(line.Sku))
            {
                errors[$"lines[{index}].sku"] = ["A SKU is required."];
            }

            if (line.Quantity is < 1 or > MaxQuantity)
            {
                errors[$"lines[{index}].quantity"] = [$"A quantity of 1 to {MaxQuantity} is required."];
            }
        }

        return errors;
    }
}
