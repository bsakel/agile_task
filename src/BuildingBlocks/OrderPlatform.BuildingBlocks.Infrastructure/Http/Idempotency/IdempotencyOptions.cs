namespace OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;

/// <summary>Configuration section <c>Idempotency</c> (ADR-0020).</summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    public const string HeaderName = "Idempotency-Key";

    /// <summary>Response header set when a stored response is returned instead of executing the request again.</summary>
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    public const int MaxKeyLength = 255;

    /// <summary>How long completed records are kept before the daily cleanup removes them.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long a request in progress blocks repeats of the same key with <c>409</c>.</summary>
    public TimeSpan InProgressLease { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Cron expression (UTC) of the cleanup message.</summary>
    public string CleanupSchedule { get; set; } = "0 3 * * *";
}
