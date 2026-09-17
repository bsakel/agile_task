using System.Net.Http.Json;
using OrderPlatform.Api.Diagnostics;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>PR 1b criterion: a missing flag evaluates to off; flagged behaviour is tested in both states (ADR-0019).</summary>
public sealed class FeatureFlagTests(PlatformFixture platform)
{
    [Fact]
    public async Task Flag_without_configuration_evaluates_to_off()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        var echo = await EchoAsync(client);

        echo.FeatureFlags[DiagnosticsFeatureFlags.EchoUppercase].ShouldBeFalse();
        echo.Message.ShouldBe("flag probe");
    }

    [Theory]
    [MemberData(nameof(FeatureFlagStates.OnAndOff), MemberType = typeof(FeatureFlagStates))]
    public async Task Echo_is_upper_case_only_when_the_flag_is_on(bool enabled)
    {
        var api = platform.ApiWith(FeatureFlagStates.Settings((DiagnosticsFeatureFlags.EchoUppercase, enabled)));
        using var client = api.CreateClient(await platform.Tokens.AcmeErpAsync());

        var echo = await EchoAsync(client);

        echo.FeatureFlags[DiagnosticsFeatureFlags.EchoUppercase].ShouldBe(enabled);
        echo.Message.ShouldBe(enabled ? "FLAG PROBE" : "flag probe");
    }

    private static async Task<EchoResult> EchoAsync(HttpClient client)
    {
        using var response = await client.PostEchoAsync("""{"message":"flag probe"}""");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EchoResult>(TestContext.Current.CancellationToken))!;
    }
}
