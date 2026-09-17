using System.Net;
using System.Text.RegularExpressions;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>PR 1b criterion: errors are problem details with a stable errorCode and the traceId (ADR-0020).</summary>
public sealed partial class ProblemDetailsTests(PlatformFixture platform)
{
    public static TheoryData<string, HttpStatusCode, string> BusinessAndValidationErrors => new()
    {
        { """{"message":""}""", HttpStatusCode.BadRequest, "validation-failed" },
        { """{"message":"x","amount":{"amount":1.5,"currency":"EUR"}}""", HttpStatusCode.BadRequest, "bad-request" },
        { """{"message":""", HttpStatusCode.BadRequest, "bad-request" },
        { $$"""{"message":"x","accountId":"{{KeycloakTokens.GlobexAccountId}}"}""", HttpStatusCode.NotFound, "resource-not-found" },
        { """{"message":"x","simulate":"NotFound"}""", HttpStatusCode.NotFound, "diagnostics-not-found" },
        { """{"message":"x","simulate":"Conflict"}""", HttpStatusCode.Conflict, "diagnostics-conflict" },
        { """{"message":"x","simulate":"BusinessRule"}""", HttpStatusCode.UnprocessableEntity, "diagnostics-business-rule" },
        { """{"message":"x","simulate":"Exception"}""", HttpStatusCode.InternalServerError, "internal-error" },
    };

    [Theory]
    [MemberData(nameof(BusinessAndValidationErrors))]
    public async Task Error_is_problem_details_with_error_code_and_trace_id(string body, HttpStatusCode status, string errorCode)
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostEchoAsync(body);

        response.StatusCode.ShouldBe(status);
        AssertProblem(await response.ReadProblemAsync(), status, errorCode);
    }

    [Fact]
    public async Task Framework_errors_are_problem_details_too()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var notFound = await client.GetAsync("/v1/does-not-exist", TestContext.Current.CancellationToken);
        using var wrongMethod = await client.GetAsync(EchoRequests.Path, TestContext.Current.CancellationToken);

        AssertProblem(await notFound.ReadProblemAsync(), HttpStatusCode.NotFound, "not-found");
        AssertProblem(await wrongMethod.ReadProblemAsync(), HttpStatusCode.MethodNotAllowed, "method-not-allowed");
    }

    [Fact]
    public async Task Exception_details_are_not_returned()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostEchoAsync("""{"message":"x","simulate":"Exception"}""");

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("Simulated unhandled exception");
    }

    private static void AssertProblem(ProblemResponse problem, HttpStatusCode status, string errorCode)
    {
        problem.Status.ShouldBe((int)status);
        problem.ErrorCode.ShouldBe(errorCode);
        problem.Type.ShouldBe($"https://problems.orderplatform.example/{errorCode}");
        problem.TraceId.ShouldNotBeNull().ShouldMatch(TraceId().ToString());
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceId();
}
