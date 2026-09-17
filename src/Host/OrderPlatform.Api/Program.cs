using JasperFx;
using OrderPlatform.Composition;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderPlatform(requireMigratedSchema: true);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOrderPlatformModules();

// Also exposes the JasperFx/Wolverine command line (e.g. "codegen write" during the image build, ADR-0021).
return await app.RunJasperFxCommands(args);
