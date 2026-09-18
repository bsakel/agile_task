using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using OrderPlatform.Billing.Application.Integration;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace OrderPlatform.Billing.Infrastructure.Http;

public static class BillingHttpGatewayExtensions
{
    /// <summary>
    /// Registers <see cref="BillingHttpGateway"/> as a typed <see cref="HttpClient"/> with the per-call protection of
    /// ADR-0014: one timeout per attempt, at most one retry and only for idempotent calls, and a circuit breaker that
    /// makes calls to a failing provider fail immediately. Longer recovery belongs to the Wolverine scheduled retries of
    /// the calling handler, not here.
    /// </summary>
    public static IServiceCollection AddBillingHttpGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new BillingHttpOptions();
        configuration.GetSection(BillingHttpOptions.SectionName).Bind(options);

        services.AddHttpClient<IBillingGateway, BillingHttpGateway>(client =>
            {
                client.BaseAddress = Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var baseAddress)
                    ? baseAddress
                    : throw new InvalidOperationException(
                        $"{BillingHttpOptions.SectionName}:BaseAddress must be the absolute URL of the billing provider, not '{options.BaseAddress}'.");

                // The per-attempt timeout below is the only deadline; HttpClient's own would cancel the whole pipeline.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddResilienceHandler("billing", pipeline =>
            {
                // Timeouts, retry delays and the breaker's window are wall-clock concerns. Without this the pipeline takes
                // the registered TimeProvider, which is the platform's business clock and is a FakeTimeProvider in tests —
                // its timers never fire, so nothing would ever time out (ADR-0022 replaces the clock, not the network).
                pipeline.TimeProvider = TimeProvider.System;

                // One quick retry for a transient failure, and only for idempotent methods: an unknown outcome of a POST
                // is resolved by the outcome query, never by sending the call again (ADR-0017 §7).
                var retry = new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = options.RetryDelay,
                    UseJitter = false,
                };
                retry.DisableForUnsafeHttpMethods();

                pipeline
                    .AddRetry(retry)
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = options.CircuitBreakerFailureRatio,
                        MinimumThroughput = options.CircuitBreakerMinimumCalls,
                        SamplingDuration = options.CircuitBreakerSamplingDuration,
                        BreakDuration = options.CircuitBreakerBreakDuration,
                    })
                    .AddTimeout(options.AttemptTimeout);
            });

        return services;
    }
}

/// <summary>
/// The provider's address and the resilience settings of ADR-0014, from <c>Integrations:Billing:Http</c>. The defaults
/// are the ones the example provider contract documents (<c>README.md</c> next to this file).
/// </summary>
public sealed class BillingHttpOptions
{
    public const string SectionName = "Integrations:Billing:Http";

    /// <summary>Absolute base URL of the billing provider; every deployed environment sets its own.</summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>Deadline for one attempt, retry included.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Pause before the single retry of an idempotent call.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Share of failed calls that opens the circuit.</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Calls needed in the sampling window before the ratio is considered at all.</summary>
    public int CircuitBreakerMinimumCalls { get; set; } = 10;

    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long every call fails immediately once the circuit is open.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}
