using Npgsql;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Messaging;

// Wraps the low-level Wolverine types verified in Phase 0 spike S2, so modules never use them directly.
internal sealed class RelationalOutbox(IWolverineRuntime runtime) : IRelationalOutbox
{
    public async Task<RelationalOutboxScope> EnlistAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (runtime.Storage is not IMessageDatabase database)
        {
            throw new InvalidOperationException(
                $"The relational outbox requires PostgreSQL message storage, but Wolverine is using {runtime.Storage.GetType().Name}.");
        }

        var context = new MessageContext(runtime);
        await context.EnlistInOutboxAsync(new DatabaseEnvelopeTransaction(database, transaction));
        return new RelationalOutboxScope(context, transaction);
    }
}
