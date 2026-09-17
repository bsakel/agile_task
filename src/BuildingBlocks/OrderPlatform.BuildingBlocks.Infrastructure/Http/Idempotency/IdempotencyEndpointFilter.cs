using System.Security.Cryptography;
using System.Text;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderPlatform.BuildingBlocks.Infrastructure.Security;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Http.Idempotency;

/// <summary>
/// Makes a state-changing endpoint safe to repeat (ADR-0020):
/// <list type="bullet">
/// <item>same key, same request, completed → the stored response (header <c>Idempotent-Replayed: true</c>);</item>
/// <item>same key, same request, still in progress → <c>409</c>;</item>
/// <item>same key, different request → <c>422</c>.</item>
/// </list>
/// The first request claims the key before the endpoint runs and stores the response afterwards. Responses with status
/// <c>5xx</c> and exceptions release the claim, so the client can retry.
/// </summary>
internal sealed class IdempotencyEndpointFilter(
    IDocumentStore store,
    TimeProvider timeProvider,
    IOptions<IdempotencyOptions> options,
    ILogger<IdempotencyEndpointFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var cancellationToken = http.RequestAborted;

        var key = http.Request.Headers[IdempotencyOptions.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > IdempotencyOptions.MaxKeyLength)
        {
            return ApiProblems.Problem(
                StatusCodes.Status400BadRequest,
                ErrorCodes.IdempotencyKeyRequired,
                "An Idempotency-Key header is required.",
                $"Send a unique {IdempotencyOptions.HeaderName} header (at most {IdempotencyOptions.MaxKeyLength} characters) and reuse it when retrying the same request.");
        }

        var partition = http.User.GetCallerPartition()
            ?? throw new InvalidOperationException("Idempotency keys are scoped per caller; the endpoint must require authorization.");
        var id = $"{partition}/{key}";
        var requestHash = await ComputeRequestHashAsync(http.Request, cancellationToken);

        var claim = await ClaimAsync(id, partition, key, requestHash, cancellationToken);
        switch (claim.Outcome)
        {
            case ClaimOutcome.Replay:
                await ReplayAsync(http, claim.Record!, cancellationToken);
                return Results.Empty;
            case ClaimOutcome.DifferentRequest:
                return ApiProblems.Problem(
                    StatusCodes.Status422UnprocessableEntity,
                    ErrorCodes.IdempotencyKeyReused,
                    "The Idempotency-Key was already used for a different request.",
                    "Use a new key for a new request.");
            case ClaimOutcome.InProgress:
                return ApiProblems.Problem(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.IdempotencyKeyInProgress,
                    "A request with this Idempotency-Key is still in progress.",
                    "Retry later to receive its response.");
        }

        // Execute the endpoint and its result into a buffer, so the response can be stored before it is sent.
        var responseBody = http.Response.Body;
        using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            var result = await next(context);
            await ExecuteResultAsync(result, http);
        }
        catch
        {
            await ReleaseAsync(id);
            throw;
        }
        finally
        {
            http.Response.Body = responseBody;
        }

        if (http.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            await ReleaseAsync(id);
        }
        else
        {
            await CompleteAsync(claim.Record!, http.Response, buffer.ToArray(), cancellationToken);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(responseBody, cancellationToken);
        return Results.Empty;
    }

    private async Task<Claim> ClaimAsync(string id, string partition, string key, string requestHash, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var session = store.LightweightSession();

        var record = await session.LoadAsync<IdempotencyRecord>(id, cancellationToken);
        if (record is not null)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new Claim(ClaimOutcome.DifferentRequest, record);
            }

            if (record.Status == IdempotencyStatus.Completed)
            {
                return new Claim(ClaimOutcome.Replay, record);
            }

            if (record.LeaseExpiresAt > now)
            {
                return new Claim(ClaimOutcome.InProgress, record);
            }

            // The previous attempt ended without releasing its claim (e.g. a crash): take it over.
            record.LeaseExpiresAt = now + options.Value.InProgressLease;
            session.Update(record);
        }
        else
        {
            record = new IdempotencyRecord
            {
                Id = id,
                Partition = partition,
                Key = key,
                RequestHash = requestHash,
                Status = IdempotencyStatus.InProgress,
                CreatedAt = now,
                LeaseExpiresAt = now + options.Value.InProgressLease,
            };
            session.Insert(record);
        }

        try
        {
            await session.SaveChangesAsync(cancellationToken);
            return new Claim(ClaimOutcome.Claimed, record);
        }
        catch (Exception exception) when (IsConcurrentClaim(exception))
        {
            return new Claim(ClaimOutcome.InProgress, record);
        }
    }

    private async Task CompleteAsync(IdempotencyRecord record, HttpResponse response, byte[] body, CancellationToken cancellationToken)
    {
        record.Status = IdempotencyStatus.Completed;
        record.ResponseStatusCode = response.StatusCode;
        record.ResponseContentType = response.ContentType;
        record.ResponseLocation = response.Headers.Location.ToString() is { Length: > 0 } location ? location : null;
        record.ResponseBody = body.Length > 0 ? Encoding.UTF8.GetString(body) : null;

        try
        {
            await using var session = store.LightweightSession();
            session.Update(record);
            await session.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // The request itself succeeded; a repeat within the lease gets 409, afterwards the claim can be taken over.
            logger.LogWarning(exception, "Could not store the response for idempotency record {IdempotencyRecordId}", record.Id);
        }
    }

    private async Task ReleaseAsync(string id)
    {
        try
        {
            await using var session = store.LightweightSession();
            session.Delete<IdempotencyRecord>(id);
            await session.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not release idempotency record {IdempotencyRecordId}; it expires with its lease", id);
        }
    }

    private static async Task ReplayAsync(HttpContext http, IdempotencyRecord record, CancellationToken cancellationToken)
    {
        http.Response.StatusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;
        http.Response.Headers[IdempotencyOptions.ReplayedHeaderName] = "true";

        if (record.ResponseLocation is not null)
        {
            http.Response.Headers.Location = record.ResponseLocation;
        }

        if (record.ResponseBody is not null)
        {
            http.Response.ContentType = record.ResponseContentType;
            await http.Response.WriteAsync(record.ResponseBody, Encoding.UTF8, cancellationToken);
        }
    }

    private static Task ExecuteResultAsync(object? result, HttpContext http) => result switch
    {
        IResult httpResult => httpResult.ExecuteAsync(http),
        null => Task.CompletedTask,
        string text => Results.Text(text).ExecuteAsync(http),
        _ => Results.Json(result).ExecuteAsync(http),
    };

    private static async Task<string> ComputeRequestHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.Body.CanSeek)
        {
            throw new InvalidOperationException(
                "The request body cannot be re-read; add app.UseIdempotencyKeys() before the endpoints are mapped.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));

        request.Body.Position = 0;
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool IsConcurrentClaim(Exception exception) =>
        exception is DocumentAlreadyExistsException or ConcurrencyException
        || (exception.InnerException is { } inner && IsConcurrentClaim(inner));

    private enum ClaimOutcome
    {
        Claimed,
        Replay,
        InProgress,
        DifferentRequest,
    }

    private sealed record Claim(ClaimOutcome Outcome, IdempotencyRecord? Record);
}
