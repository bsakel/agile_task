using OrderPlatform.BuildingBlocks.FeatureFlags;

namespace OrderPlatform.Api.Diagnostics;

/// <summary>Flags of the development-only diagnostics endpoint; follows the per-module registry convention (ADR-0019).</summary>
public static class DiagnosticsFeatureFlags
{
    /// <summary>When on, the echo endpoint returns the message in upper case. Off (the default) returns it unchanged.</summary>
    [FeatureFlag(FeatureFlagType.Release, Owner = "platform-team", Expires = "2026-12-31")]
    public const string EchoUppercase = "Diagnostics.EchoUppercase";
}
