using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using OrderPlatform.BuildingBlocks.Infrastructure.Http;
using OrderPlatform.BuildingBlocks.Infrastructure.Security;

namespace OrderPlatform.Api.Security;

/// <summary>Authentication, authorization and rate limiting (ADR-0016).</summary>
internal static class SecurityExtensions
{
    public static WebApplicationBuilder AddPlatformSecurity(this WebApplicationBuilder builder)
    {
        // JWT bearer tokens from the OpenID Connect provider (Keycloak locally). Authority, MetadataAddress,
        // ValidAudiences and RequireHttpsMetadata are bound from Authentication:Schemes:Bearer.
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the provider's claim names (sub, scope, roles, account_id) instead of legacy SOAP claim types.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = PlatformClaims.Subject;
                options.TokenValidationParameters.RoleClaimType = PlatformClaims.Roles;
            });

        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Authority) || !string.IsNullOrWhiteSpace(options.MetadataAddress),
                "Authentication:Schemes:Bearer:Authority (or MetadataAddress) must be configured.")
            .Validate(
                options => options.TokenValidationParameters.ValidAudiences?.Any() == true,
                "Authentication:Schemes:Bearer:ValidAudiences must be configured.")
            .ValidateOnStart();

        builder.Services.AddAuthorizationBuilder()
            // Deny by default: every endpoint requires an authenticated caller unless it explicitly allows anonymous access.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(PlatformPolicies.CustomerAccount, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.GetAccountId() is not null))
            .AddPolicy(PlatformPolicies.SupportAgent, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(PlatformClaims.Roles, PlatformClaims.SupportAgentRole));

        foreach (var scope in PlatformScopes.All)
        {
            builder.Services.AddAuthorizationBuilder().AddPolicy(scope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasScope(scope)));
        }

        builder.AddPartitionedRateLimiting();

        return builder;
    }

    private static void AddPartitionedRateLimiting(this WebApplicationBuilder builder)
    {
        var limits = builder.Configuration.GetSection(PlatformRateLimits.SectionName).Get<PlatformRateLimits>() ?? new PlatformRateLimits();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Default limit for every API request, partitioned by caller (account, agent, or remote address when anonymous).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/" + Conventions.ApiConventionsExtensions.ApiVersion)
                    ? FixedWindow(PartitionKey(context), limits.Default)
                    : RateLimitPartition.GetNoLimiter("unlimited"));

            options.AddPolicy(PlatformRateLimits.OrderSubmission, context => FixedWindow(PartitionKey(context), limits.OrderSubmissionLimit));

            options.OnRejected = async (rejection, cancellationToken) =>
            {
                var http = rejection.HttpContext;
                if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problem = ApiProblems.Problem(
                    StatusCodes.Status429TooManyRequests,
                    ErrorCodes.RateLimited,
                    "Too many requests.",
                    "The rate limit for this account was exceeded; retry after the time given in the Retry-After header.");
                await problem.ExecuteAsync(http);
            };
        });
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.GetCallerPartition() ?? $"anonymous:{context.Connection.RemoteIpAddress}";

    private static RateLimitPartition<string> FixedWindow(string partitionKey, FixedWindowLimit limit) =>
        RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
            QueueLimit = 0,
        });
}
