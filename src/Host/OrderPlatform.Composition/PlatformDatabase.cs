namespace OrderPlatform.Composition;

public static class PlatformDatabase
{
    /// <summary>Connection string name, shared by Aspire (<c>AddDatabase</c>), compose and deployment.</summary>
    public const string ConnectionName = "orderplatform";

    /// <summary>Schema owned by Marten: event store, documents and projections of the Ordering module (ADR-0006).</summary>
    public const string MartenSchema = "ordering";

    /// <summary>Schema owned by Wolverine: inbox, outbox, scheduled messages and dead letters (ADR-0005).</summary>
    public const string WolverineSchema = "wolverine";

    /// <summary>Schema owned by the Migrator itself: its run history.</summary>
    public const string PlatformSchema = "platform";
}
