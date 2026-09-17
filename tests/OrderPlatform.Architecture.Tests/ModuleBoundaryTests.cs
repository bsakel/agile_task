using System.Text.RegularExpressions;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace OrderPlatform.Architecture.Tests;

/// <summary>Type-level dependency rules between and inside modules (ADR-0003, ADR-0015).</summary>
public sealed class ModuleBoundaryTests
{
    private const string Frameworks = @"^(Marten|Wolverine|Dapper|Npgsql|Weasel|JasperFx|Microsoft\.AspNetCore)(\.|$)";

    public static TheoryData<string> Modules => [.. PlatformArchitecture.Modules];

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_module_uses_other_modules_only_through_their_contracts(string module)
    {
        var others = string.Join("|", PlatformArchitecture.Modules.Where(other => other != module).Select(Regex.Escape));

        Types().That().ResideInNamespaceMatching(ModuleNamespace(module))
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching($@"^OrderPlatform\.({others})\.(Domain|Application|Infrastructure)(\.|$)"))
            .Because("other modules may only be referenced through their Contracts (ADR-0003)")
            .WithoutRequiringPositiveResults()
            .Check(PlatformArchitecture.Model);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_depend_only_on_technology_free_building_blocks(string module)
    {
        Types().That().ResideInNamespaceMatching(LayerNamespace(module, "Contracts"))
            .Should().NotDependOnAny(Types(true).That().ResideInNamespaceMatching(
                $@"{Frameworks}|^OrderPlatform\.BuildingBlocks\.Infrastructure(\.|$)|^OrderPlatform\.\w+\.(Domain|Application|Infrastructure)(\.|$)"))
            .Because("Contracts are the published language between modules and carry no technology (ADR-0015)")
            .WithoutRequiringPositiveResults()
            .Check(PlatformArchitecture.Model);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_has_no_application_infrastructure_or_framework_dependencies(string module)
    {
        Types().That().ResideInNamespaceMatching(LayerNamespace(module, "Domain"))
            .Should().NotDependOnAny(Types(true).That().ResideInNamespaceMatching(
                $@"{Frameworks}|^OrderPlatform\.BuildingBlocks\.Infrastructure(\.|$)|^OrderPlatform\.\w+\.(Application|Infrastructure)(\.|$)"))
            .Because("the domain is pure and testable without infrastructure (ADR-0003)")
            .WithoutRequiringPositiveResults()
            .Check(PlatformArchitecture.Model);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_does_not_depend_on_infrastructure_or_http(string module)
    {
        Types().That().ResideInNamespaceMatching(LayerNamespace(module, "Application"))
            .Should().NotDependOnAny(Types(true).That().ResideInNamespaceMatching(
                @"^Microsoft\.AspNetCore(\.|$)|^OrderPlatform\.\w+\.Infrastructure(\.|$)"))
            .Because("handlers know nothing about HTTP or adapters (ADR-0011, ADR-0014)")
            .WithoutRequiringPositiveResults()
            .Check(PlatformArchitecture.Model);
    }

    [Fact]
    public void Only_the_feature_flag_adapter_uses_Microsoft_FeatureManagement()
    {
        Types().That().DoNotResideInNamespaceMatching(@"^OrderPlatform\.Api\.FeatureFlags$")
            .Should().NotDependOnAny(Types(true).That().ResideInNamespaceMatching(@"^Microsoft\.FeatureManagement(\.|$)"))
            .Because("application code uses IFeatureFlags so the flag library can be replaced (ADR-0019)")
            .Check(PlatformArchitecture.Model);
    }

    [Fact]
    public void Fake_adapters_live_in_an_infrastructure_Fakes_namespace()
    {
        IArchRule rule = Classes().That().HaveNameStartingWith("Fake")
            .Should().ResideInNamespaceMatching(@"^OrderPlatform\.\w+\.Infrastructure\.Fakes(\.|$)")
            .Because("fakes must stay out of production code paths and be guarded by the integration mode (ADR-0014)")
            .WithoutRequiringPositiveResults();

        rule.Check(PlatformArchitecture.Model);
    }

    private static string ModuleNamespace(string module) => $@"^OrderPlatform\.{module}(\.|$)";

    private static string LayerNamespace(string module, string layer) => $@"^OrderPlatform\.{module}\.{layer}(\.|$)";
}
