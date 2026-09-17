using Xunit;

namespace OrderPlatform.Testing;

/// <summary>
/// Helper for ADR-0019 rule 3 ("both states tested"): theory data for a flag on and off, and the configuration entries that
/// switch flags for a test host.
/// </summary>
public static class FeatureFlagStates
{
    /// <summary>Use with <c>[MemberData(nameof(FeatureFlagStates.OnAndOff), MemberType = typeof(FeatureFlagStates))]</c>.</summary>
    public static TheoryData<bool> OnAndOff => [true, false];

    /// <summary>Configuration entries (<c>FeatureManagement:&lt;flag&gt;</c>) for the given flag states.</summary>
    public static IReadOnlyDictionary<string, string?> Settings(params (string Flag, bool Enabled)[] flags) =>
        flags.ToDictionary(flag => $"FeatureManagement:{flag.Flag}", flag => (string?)(flag.Enabled ? "true" : "false"));
}
