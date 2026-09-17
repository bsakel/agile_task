namespace OrderPlatform.BuildingBlocks.FeatureFlags;

/// <summary>
/// Registers a flag. Applied to a <c>const string</c> field of a module's <c>FeatureFlags</c> class, which is the
/// single registry of that module's flags (ADR-0019 rule 1).
/// </summary>
/// <example>
/// <code>
/// public static class FeatureFlags
/// {
///     [FeatureFlag(FeatureFlagType.Release, Owner = "team-pricing", Expires = "2026-12-31")]
///     public const string ShippingCharge = "Pricing.ShippingCharge";
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class FeatureFlagAttribute(FeatureFlagType type) : Attribute
{
    public FeatureFlagType Type { get; } = type;

    /// <summary>Team or person responsible for switching and removing the flag.</summary>
    public required string Owner { get; init; }

    /// <summary>ISO date (<c>yyyy-MM-dd</c>) by which the flag must be removed. Required for release flags.</summary>
    public string? Expires { get; init; }
}

public enum FeatureFlagType
{
    /// <summary>Separates deployment from release; removed after a stabilisation period.</summary>
    Release,

    /// <summary>Turns existing behaviour off during an incident; may stay.</summary>
    KillSwitch,
}
