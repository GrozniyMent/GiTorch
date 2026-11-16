using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services;
using gitorch.Services.Abstractions;

namespace gitorch.Endpoints;

public static class WebhookEndpoints
{
    private const int MaxWebhookBodyBytes = 1024 * 1024; // 1 MB

    public static WebApplication MapWebhookEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapPost("/webhook", HandleWebhookAsync);
        return app;
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext ctx,
        OrchestratorConfig cfg,
        IJobQueue queue,
        OrchestratorState state,
        RunnerWorker worker,
        ILoggerFactory loggerFactory)
    {
        var eventType = ctx.Request.Headers["X-GitHub-Event"].FirstOrDefault();
        if (eventType == "ping") return Results.Ok("pong");
        if (eventType != "workflow_job") return Results.Ok("ignored event");

        if (ctx.Request.ContentLength > MaxWebhookBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var (tooLarge, bodyBytes) = await ReadBoundedBodyAsync(ctx.Request.Body, ctx.RequestAborted);
        if (tooLarge)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var sigHeader = ctx.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (!VerifySignature(bodyBytes, sigHeader, cfg.WebhookSecret))
        {
            loggerFactory.CreateLogger("Webhook").LogWarning("Invalid HMAC signature on webhook");
            return Results.Unauthorized();
        }

        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(bodyBytes);
        }
        catch (JsonException)
        {
            return Results.BadRequest("Invalid JSON payload");
        }

        if (payload?.WorkflowJob is not { } job ||
            payload.Repository?.Owner is not { } owner ||
            string.IsNullOrWhiteSpace(owner.Login) ||
            string.IsNullOrWhiteSpace(payload.Repository.Name) ||
            payload.Installation is not { } installation)
        {
            return Results.BadRequest("Malformed workflow_job payload");
        }

        var labels = job.Labels ?? [];
        var logger = loggerFactory.CreateLogger("Webhook");
        var deliveryId = ctx.Request.Headers["X-GitHub-Delivery"].FirstOrDefault();

        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobId"] = job.Id,
            ["Owner"] = owner.Login,
            ["Repo"] = payload.Repository.Name,
            ["DeliveryId"] = deliveryId
        });

        if (payload.Action == "queued" &&
            labels.Contains(cfg.RequiredLabel, StringComparer.OrdinalIgnoreCase))
        {
            var isOrg = string.Equals(owner.Type, "Organization", StringComparison.OrdinalIgnoreCase);
            var jobReq = new JobRequest(
                JobId: job.Id,
                Labels: labels,
                InstallationId: installation.Id,
                Owner: owner.Login,
                RepoName: payload.Repository.Name,
                IsOrganization: isOrg);

            if (!state.TryRegisterQueued(jobReq, deliveryId))
            {
                logger.LogInformation("Duplicate webhook ignored for job {JobId} (delivery {DeliveryId})",
                    job.Id, deliveryId ?? "none");
                return Results.Ok("duplicate ignored");
            }

            if (!await queue.EnqueueAsync(jobReq, ctx.RequestAborted))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            logger.LogInformation("Queued job {JobId} for {Owner}/{Repo}", job.Id, jobReq.Owner, jobReq.RepoName);
        }
        else if (payload.Action is "completed" or "cancelled")
        {
            var reason = $"GitHub job {job.Conclusion ?? payload.Action}";
            await worker.CancelJobAsync(job.Id, reason);
            logger.LogInformation("Processed {Action} webhook for job {JobId} ({Reason})",
                payload.Action, job.Id, reason);
        }

        return Results.Accepted();
    }

    private static async Task<(bool TooLarge, byte[] Body)> ReadBoundedBodyAsync(Stream body, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0)
                    break;

                if (ms.Length + read > MaxWebhookBodyBytes)
                    return (true, []);

                ms.Write(buffer, 0, read);
            }

            return (false, ms.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static bool VerifySignature(byte[] payload, string? signatureWithPrefix, string secret)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrEmpty(signatureWithPrefix) || !signatureWithPrefix.StartsWith(prefix))
            return false;

        var expectedHex = signatureWithPrefix[prefix.Length..];
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(keyBytes, payload);
        var actualHex = Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(actualHex));
    }
}
