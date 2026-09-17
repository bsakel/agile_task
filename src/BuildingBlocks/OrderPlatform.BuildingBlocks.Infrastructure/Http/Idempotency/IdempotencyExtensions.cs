using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;

public static class IdempotencyExtensions
{
    /// <summary>Registers the filter, its options and the daily cleanup schedule.</summary>
    public static IServiceCollection AddIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyOptions>().Bind(configuration.GetSection(IdempotencyOptions.SectionName));
        services.AddSingleton<IdempotencyEndpointFilter>();
        return services;
    }

    /// <summary>Registers the record as a Marten document; the Migrator creates its table (ADR-0008).</summary>
    public static void ConfigureIdempotency(this StoreOptions options) =>
        options.Schema.For<IdempotencyRecord>()
            .UseOptimisticConcurrency(true)
            // Concurrent index creation, so adding it to a populated table does not block writes (ADR-0009 rule 1).
            .Index(record => record.CreatedAt, index => index.IsConcurrent = true);

    /// <summary>Discovers the cleanup handler and schedules the cleanup message.</summary>
    public static void ConfigureIdempotency(this WolverineOptions options, IConfiguration configuration)
    {
        var settings = configuration.GetSection(IdempotencyOptions.SectionName).Get<IdempotencyOptions>() ?? new IdempotencyOptions();

        options.Discovery.IncludeType(typeof(CleanUpIdempotencyRecordsHandler));
        options.Schedules.ScheduleRecurring<CleanUpIdempotencyRecords>(settings.CleanupSchedule);
    }

    /// <summary>
    /// Buffers the body of requests that carry an <c>Idempotency-Key</c>, so the filter can hash it after model binding.
    /// </summary>
    public static IApplicationBuilder UseIdempotencyKeys(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (context.Request.Headers.ContainsKey(IdempotencyOptions.HeaderName))
            {
                context.Request.EnableBuffering();
            }

            return next(context);
        });

    /// <summary>
    /// Requires an <c>Idempotency-Key</c> header on a state-changing endpoint and makes repeats return the stored response
    /// (ADR-0020). The endpoint must require an authenticated caller: keys are scoped per account.
    /// </summary>
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder) =>
        builder
            .AddEndpointFilter<RouteHandlerBuilder, IdempotencyEndpointFilter>()
            .WithMetadata(new IdempotencyKeyRequiredMetadata())
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}

/// <summary>Endpoint metadata marking endpoints that require an <c>Idempotency-Key</c> header (e.g. for the OpenAPI document).</summary>
public sealed class IdempotencyKeyRequiredMetadata;
