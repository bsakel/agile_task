using JasperFx.Metadata;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;

/// <summary>
/// A request made with an <c>Idempotency-Key</c>, stored as a Marten document in the <c>ordering</c> schema (ADR-0020).
/// The id combines the caller partition (account or agent) and the key, so keys are scoped per account.
/// </summary>
public sealed class IdempotencyRecord : IVersioned
{
    public string Id { get; set; } = "";

    /// <summary><c>account:&lt;id&gt;</c> or <c>agent:&lt;sub&gt;</c>.</summary>
    public string Partition { get; set; } = "";

    public string Key { get; set; } = "";

    /// <summary>SHA-256 of method, path, query and body: the same key with a different request is rejected.</summary>
    public string RequestHash { get; set; } = "";

    public IdempotencyStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// While <see cref="IdempotencyStatus.InProgress"/>: until when the claim blocks repeats with <c>409</c>. A claim left
    /// behind by a crashed request can be taken over after it expires.
    /// </summary>
    public DateTimeOffset LeaseExpiresAt { get; set; }

    public int? ResponseStatusCode { get; set; }

    public string? ResponseContentType { get; set; }

    public string? ResponseLocation { get; set; }

    public string? ResponseBody { get; set; }

    /// <summary>Optimistic concurrency: two requests cannot take over the same expired claim.</summary>
    public Guid Version { get; set; }
}

public enum IdempotencyStatus
{
    InProgress,
    Completed,
}
