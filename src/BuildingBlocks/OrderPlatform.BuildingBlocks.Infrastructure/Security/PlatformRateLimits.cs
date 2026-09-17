namespace OrderPlatform.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Configuration section <c>RateLimiting</c> (ADR-0016). Every <c>/v1</c> request counts against the caller's default
/// limit; endpoints can add a stricter named policy, e.g. <c>RequireRateLimiting(PlatformRateLimits.OrderSubmission)</c>.
/// </summary>
public sealed class PlatformRateLimits
{
    public const string SectionName = "RateLimiting";

    /// <summary>Stricter policy for <c>POST /v1/orders</c>.</summary>
    public const string OrderSubmission = "order-submission";

    public FixedWindowLimit Default { get; set; } = new() { PermitLimit = 600, WindowSeconds = 60 };

    public FixedWindowLimit OrderSubmissionLimit { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };
}

public sealed class FixedWindowLimit
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
