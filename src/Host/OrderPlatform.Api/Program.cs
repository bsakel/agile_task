using JasperFx;
using OrderPlatform.Api.Conventions;
using OrderPlatform.Api.Diagnostics;
using OrderPlatform.Api.FeatureFlags;
using OrderPlatform.Api.Security;
using OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;
using OrderPlatform.BuildingBlocks.Infrastructure.Integrations;
using OrderPlatform.Composition;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Registered before the platform so it runs first: fake adapters never start outside Development and Test (ADR-0014).
builder.Services.AddIntegrationModeGuard();

builder.AddOrderPlatform(requireMigratedSchema: true, wolverine => wolverine.AddDiagnosticsHandlers());
builder.AddApiConventions();
builder.AddPlatformSecurity();
builder.AddPlatformFeatureFlags(PlatformModules.FeatureFlagRegistries.Append(typeof(DiagnosticsFeatureFlags)));

var app = builder.Build();

app.UseApiConventions();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseIdempotencyKeys();

app.MapDefaultEndpoints();
app.MapOpenApi().AllowAnonymous();

// Version route group; deny by default also applies through the fallback policy (ADR-0016, ADR-0020).
var v1 = app.MapGroup("/" + ApiConventionsExtensions.ApiVersion).RequireAuthorization();
v1.MapOrderPlatformModules();

if (app.Environment.IsDevelopment())
{
    v1.MapDiagnosticsEndpoints();
}

// Also exposes the JasperFx/Wolverine command line (e.g. "codegen write" during the image build, ADR-0021).
return await app.RunJasperFxCommands(args);

/// <summary>Entry point type, public so test hosts can start the Api (WebApplicationFactory, ADR-0022).</summary>
public partial class Program;
