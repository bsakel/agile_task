using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.FeatureFlags;

namespace OrderPlatform.Api.Diagnostics;

/// <summary>Command behind <c>POST /v1/_diagnostics/echo</c>.</summary>
public sealed record EchoDiagnostics(
    Guid CallerAccountId,
    string Message,
    Money? Amount,
    Guid? TargetAccountId,
    EchoSimulation Simulate);

public enum EchoSimulation
{
    None,

    /// <summary>Takes a few seconds, so a repeat with the same Idempotency-Key observes the request in progress.</summary>
    Slow,

    NotFound,
    Conflict,
    BusinessRule,

    /// <summary>Throws an unhandled exception (500).</summary>
    Exception,
}

public sealed record EchoResponse(
    Guid EchoId,
    Guid AccountId,
    string Message,
    Money? Amount,
    DateTimeOffset ProcessedAt,
    IReadOnlyDictionary<string, bool> FeatureFlags);

/// <summary>
/// Exercises the handler-side conventions the business handlers will follow: account scoping, expected failures as
/// <see cref="Result"/>, and flag evaluation through <see cref="IFeatureFlags"/>.
/// </summary>
public static class EchoDiagnosticsHandler
{
    public static async Task<Result<EchoResponse>> Handle(
        EchoDiagnostics command,
        IFeatureFlags featureFlags,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (command.TargetAccountId is { } targetAccountId
            && AccountAccess.EnsureOwnedBy(command.CallerAccountId, targetAccountId, "account") is { IsSuccess: false } denied)
        {
            return denied.Error;
        }

        switch (command.Simulate)
        {
            case EchoSimulation.Slow:
                await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, cancellationToken);
                break;
            case EchoSimulation.NotFound:
                return Error.NotFound("diagnostics-not-found", "Simulated not found.");
            case EchoSimulation.Conflict:
                return Error.Conflict("diagnostics-conflict", "Simulated conflict with the current state.");
            case EchoSimulation.BusinessRule:
                return Error.BusinessRule("diagnostics-business-rule", "Simulated business rule violation.");
            case EchoSimulation.Exception:
                throw new InvalidOperationException("Simulated unhandled exception.");
        }

        var uppercase = await featureFlags.IsEnabledAsync(DiagnosticsFeatureFlags.EchoUppercase, cancellationToken);

        return new EchoResponse(
            Guid.CreateVersion7(),
            command.CallerAccountId,
            uppercase ? command.Message.ToUpperInvariant() : command.Message,
            command.Amount,
            timeProvider.GetUtcNow(),
            new Dictionary<string, bool> { [DiagnosticsFeatureFlags.EchoUppercase] = uppercase });
    }
}
