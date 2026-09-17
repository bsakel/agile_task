using Marten;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderPlatform.BuildingBlocks.Messaging;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;

/// <summary>Removes idempotency records older than the retention period. Sent daily by a Wolverine recurring schedule.</summary>
public sealed record CleanUpIdempotencyRecords : ICommand;

public static class CleanUpIdempotencyRecordsHandler
{
    // Wolverine applies the Marten transaction (AutoApplyTransactions) because the handler uses IDocumentSession.
    public static void Handle(
        CleanUpIdempotencyRecords message,
        IDocumentSession session,
        TimeProvider timeProvider,
        IOptions<IdempotencyOptions> options,
        ILogger<CleanUpIdempotencyRecords> logger)
    {
        var cutoff = timeProvider.GetUtcNow() - options.Value.Retention;
        session.DeleteWhere<IdempotencyRecord>(record => record.CreatedAt < cutoff);
        logger.LogInformation("Removing idempotency records created before {Cutoff}", cutoff);
    }
}
