using Npgsql;
using Wolverine;
using Wolverine.Runtime;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Transactional outbox for Dapper-based modules: messages published through the scope are stored in the
/// module's own <see cref="NpgsqlTransaction"/> and dispatched only after commit (ADR-0005, ADR-0007).
/// </summary>
public interface IRelationalOutbox
{
    Task<RelationalOutboxScope> EnlistAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken = default);
}

public sealed class RelationalOutboxScope
{
    private readonly MessageContext _context;
    private readonly NpgsqlTransaction _transaction;

    internal RelationalOutboxScope(MessageContext context, NpgsqlTransaction transaction)
    {
        _context = context;
        _transaction = transaction;
    }

    /// <summary>Publish or send messages here; they are persisted in the enlisted transaction.</summary>
    public IMessageBus Messages => _context;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken);
        await _context.FlushOutgoingMessagesAsync();
    }

    /// <summary>Rolls back the transaction; messages published in this scope are discarded with it.</summary>
    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _transaction.RollbackAsync(cancellationToken);
}
