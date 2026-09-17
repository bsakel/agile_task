using System.Globalization;
using System.Reflection;

namespace OrderPlatform.BuildingBlocks.FeatureFlags;

/// <summary>A registered flag with its registry metadata.</summary>
public sealed record FeatureFlagDefinition(string Name, FeatureFlagType Type, string Owner, DateOnly? Expires, Type Registry);

/// <summary>
/// All flags known to the application, collected from the modules' <c>FeatureFlags</c> classes. Evaluating a name that
/// is not registered is a programming error (ADR-0019 rule 1).
/// </summary>
public sealed class FeatureFlagRegistry
{
    private readonly Dictionary<string, FeatureFlagDefinition> flags;

    private FeatureFlagRegistry(Dictionary<string, FeatureFlagDefinition> flags) => this.flags = flags;

    public IReadOnlyCollection<FeatureFlagDefinition> Flags => flags.Values;

    public bool Contains(string name) => flags.ContainsKey(name);

    /// <summary>
    /// Reads every <c>const string</c> field of the registry types. Throws when a field is not annotated, a release flag
    /// has no valid expiry date, or a name is registered twice.
    /// </summary>
    public static FeatureFlagRegistry FromTypes(IEnumerable<Type> registries)
    {
        var flags = new Dictionary<string, FeatureFlagDefinition>(StringComparer.Ordinal);

        foreach (var registry in registries)
        {
            var fields = registry
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string));

            foreach (var field in fields)
            {
                var name = (string)field.GetRawConstantValue()!;
                var attribute = field.GetCustomAttribute<FeatureFlagAttribute>()
                    ?? throw new InvalidOperationException(
                        $"Feature flag '{name}' in {registry.FullName} has no [FeatureFlag] attribute with type, owner and expiry.");

                DateOnly? expires = null;
                if (attribute.Expires is not null)
                {
                    expires = DateOnly.TryParseExact(attribute.Expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        ? date
                        : throw new InvalidOperationException($"Feature flag '{name}' has an invalid expiry date '{attribute.Expires}'.");
                }
                else if (attribute.Type == FeatureFlagType.Release)
                {
                    throw new InvalidOperationException($"Release flag '{name}' must declare an expiry date.");
                }

                if (!flags.TryAdd(name, new FeatureFlagDefinition(name, attribute.Type, attribute.Owner, expires, registry)))
                {
                    throw new InvalidOperationException($"Feature flag '{name}' is registered more than once.");
                }
            }
        }

        return new FeatureFlagRegistry(flags);
    }
}
