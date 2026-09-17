using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OrderPlatform.Architecture.Tests;

/// <summary>
/// Project-level module boundaries. The compiler drops references that no code uses, so a type-level rule alone would miss
/// an illegal <c>ProjectReference</c> until someone starts using it (ADR-0003, ADR-0015).
/// </summary>
public sealed partial class ProjectReferenceTests
{
    public static TheoryData<string> ModuleProjects => [.. Directory
        .GetFiles(Path.Combine(PlatformArchitecture.RepositoryRoot.FullName, "src", "Modules"), "*.csproj", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(PlatformArchitecture.RepositoryRoot.FullName, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(ModuleProjects))]
    public void Module_projects_reference_only_what_their_layer_allows(string projectPath)
    {
        var (module, layer) = ParseModuleProject(Path.GetFileNameWithoutExtension(projectPath));
        var references = XDocument.Load(Path.Combine(PlatformArchitecture.RepositoryRoot.FullName, projectPath))
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();

        var violations = references.Where(reference => !IsAllowed(module, layer, reference)).ToList();

        violations.ShouldBeEmpty(
            $"{projectPath} ({layer}) may reference: {DescribeAllowed(layer)}. See architecture/adr/0015-architecture-tests-enforce-boundaries.md");
    }

    [Fact]
    public void Every_module_has_the_four_layer_projects()
    {
        var projects = ModuleProjects.Select(row => Path.GetFileNameWithoutExtension(row.Data)).ToHashSet(StringComparer.Ordinal);

        foreach (var module in PlatformArchitecture.Modules)
        {
            foreach (var layer in Layers)
            {
                projects.ShouldContain($"{module}.{layer}");
            }
        }
    }

    [Fact]
    public void Only_the_Api_host_references_Microsoft_FeatureManagement()
    {
        var offenders = Directory
            .GetFiles(Path.Combine(PlatformArchitecture.RepositoryRoot.FullName, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => Path.GetFileNameWithoutExtension(path) != "OrderPlatform.Api")
            .Where(path => XDocument.Load(path).Descendants("PackageReference")
                .Any(package => package.Attribute("Include")?.Value.StartsWith("Microsoft.FeatureManagement", StringComparison.Ordinal) == true))
            .Select(path => Path.GetRelativePath(PlatformArchitecture.RepositoryRoot.FullName, path))
            .ToList();

        offenders.ShouldBeEmpty("application code uses IFeatureFlags; only the Api host's adapter uses Microsoft.FeatureManagement (ADR-0019)");
    }

    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Contracts"];

    private static bool IsAllowed(string module, string layer, string reference)
    {
        if (reference == "OrderPlatform.BuildingBlocks")
        {
            return true;
        }

        if (reference == "OrderPlatform.BuildingBlocks.Infrastructure")
        {
            return layer == "Infrastructure";
        }

        var match = ModuleProjectName().Match(reference);
        if (!match.Success)
        {
            // Hosts, tools and anything else outside the module template.
            return false;
        }

        var (referencedModule, referencedLayer) = (match.Groups["module"].Value, match.Groups["layer"].Value);
        var sameModule = referencedModule == module;

        return layer switch
        {
            "Contracts" => false,
            "Domain" => false,
            "Application" => referencedLayer == "Contracts" || (sameModule && referencedLayer == "Domain"),
            "Infrastructure" => referencedLayer == "Contracts" || (sameModule && referencedLayer is "Domain" or "Application"),
            _ => false,
        };
    }

    private static string DescribeAllowed(string layer) => layer switch
    {
        "Contracts" or "Domain" => "OrderPlatform.BuildingBlocks only",
        "Application" => "OrderPlatform.BuildingBlocks, its own Domain, any module's Contracts",
        _ => "OrderPlatform.BuildingBlocks(.Infrastructure), its own Domain and Application, any module's Contracts",
    };

    private static (string Module, string Layer) ParseModuleProject(string projectName)
    {
        var match = ModuleProjectName().Match(projectName);
        match.Success.ShouldBeTrue($"Module project '{projectName}' does not follow the <Module>.<Layer> naming (ADR-0003).");
        return (match.Groups["module"].Value, match.Groups["layer"].Value);
    }

    [GeneratedRegex("^(?<module>[A-Z][A-Za-z]+)\\.(?<layer>Domain|Application|Infrastructure|Contracts)$")]
    private static partial Regex ModuleProjectName();
}
