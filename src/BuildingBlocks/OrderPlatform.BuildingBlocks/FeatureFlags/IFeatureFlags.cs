namespace OrderPlatform.BuildingBlocks.FeatureFlags;

/// <summary>
/// Evaluates feature flags. Application code depends on this interface only, never on the flag library, so the v1
/// implementation (Microsoft.FeatureManagement) can be replaced by OpenFeature with one adapter (ADR-0019).
/// </summary>
public interface IFeatureFlags
{
    /// <summary>
    /// Returns whether the flag is on. A flag without configuration is off, and off is always the existing behaviour.
    /// </summary>
    /// <param name="flag">A registered flag name, i.e. a constant of a module's <c>FeatureFlags</c> class.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> IsEnabledAsync(string flag, CancellationToken ct = default);
}
