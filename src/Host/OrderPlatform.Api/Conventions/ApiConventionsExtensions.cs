using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.OpenApi;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.Infrastructure.Http;
using OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;
using OrderPlatform.BuildingBlocks.Infrastructure.Json;

namespace OrderPlatform.Api.Conventions;

/// <summary>HTTP API conventions: problem details, JSON payloads and the OpenAPI document (ADR-0020).</summary>
internal static class ApiConventionsExtensions
{
    public const string ApiVersion = "v1";

    public static WebApplicationBuilder AddApiConventions(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;

            // Framework responses (authentication, routing, binding, exceptions) get a code from their status.
            if (!problem.Extensions.ContainsKey(ApiProblems.ErrorCodeExtension))
            {
                var errorCode = ErrorCodes.ForStatus(status);
                problem.Extensions[ApiProblems.ErrorCodeExtension] = errorCode;
                problem.Type = ApiProblems.TypeFor(errorCode);
            }

            // The W3C trace id, searchable in the tracing backend (ADR-0012).
            problem.Extensions[ApiProblems.TraceIdExtension] =
                Activity.Current?.TraceId.ToHexString() ?? context.HttpContext.TraceIdentifier;

            // Exception messages and stack traces are never returned outside Development (ADR-0016).
            if (status >= StatusCodes.Status500InternalServerError && !builder.Environment.IsDevelopment())
            {
                problem.Detail = null;
                problem.Extensions.Remove("exception");
            }
        });

        // Malformed requests become 400 problem details instead of exceptions, also in Development.
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

        builder.Services.Configure<JsonOptions>(options =>
        {
            // Web defaults: camelCase, case-insensitive reading; unknown request fields are ignored.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.Converters.Add(new MoneyJsonConverter());
            options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        });

        builder.Services.AddOpenApi(ApiVersion, options =>
        {
            options.ShouldInclude = description =>
                description.RelativePath?.StartsWith(ApiVersion + "/", StringComparison.Ordinal) == true;

            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Order Processing Platform API",
                    Version = ApiVersion,
                    Description =
                        "Changes within v1 are additive only. Clients must ignore unknown response fields and tolerate unknown " +
                        "enum values (e.g. new order statuses). Errors are RFC 9457 problem details with a stable errorCode " +
                        "and a traceId. Money amounts are decimal strings; timestamps are UTC ISO 8601.",
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "OAuth2 access token from the platform's identity provider (client credentials or authorization code with PKCE).",
                };
                document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotencyKeyRequiredMetadata>().Any())
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = IdempotencyOptions.HeaderName,
                        In = ParameterLocation.Header,
                        Required = true,
                        Description = "Unique per request; reuse it when retrying. Scoped per account, kept for 24 hours.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = IdempotencyOptions.MaxKeyLength },
                    });
                }

                return Task.CompletedTask;
            });

            options.AddSchemaTransformer((schema, context, _) =>
            {
                var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
                if (type == typeof(Money))
                {
                    schema.Type = JsonSchemaType.Object;
                    schema.Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["amount"] = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = @"^-?\d+(\.\d+)?$", Examples = [JsonValue.Create("1234.50")] },
                        ["currency"] = new OpenApiSchema { Type = JsonSchemaType.String, Examples = [JsonValue.Create(Money.Eur)] },
                    };
                    schema.Required = new HashSet<string> { "amount", "currency" };
                }
                else if (type == typeof(DateTimeOffset))
                {
                    // Custom converters hide the type from the schema generator.
                    schema.Type = context.JsonTypeInfo.Type == type ? JsonSchemaType.String : JsonSchemaType.String | JsonSchemaType.Null;
                    schema.Format = "date-time";
                    schema.Examples = [JsonValue.Create("2026-09-17T10:15:00Z")];
                }

                return Task.CompletedTask;
            });
        });

        return builder;
    }

    public static WebApplication UseApiConventions(this WebApplication app)
    {
        // Unhandled exceptions and empty error responses (401, 403, 404, 405, ...) become problem details.
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        return app;
    }
}
