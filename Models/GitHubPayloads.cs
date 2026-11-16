using System.Text.Json.Serialization;

namespace gitorch.Models;

public sealed record WebhookPayload(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("workflow_job")] WorkflowJobPayload? WorkflowJob,
    [property: JsonPropertyName("repository")] RepositoryPayload Repository,
    [property: JsonPropertyName("installation")] InstallationPayload Installation);

public sealed record WorkflowJobPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("labels")] string[] Labels,
    [property: JsonPropertyName("conclusion")] string? Conclusion = null);

public sealed record RepositoryPayload(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("owner")] OwnerPayload Owner);

public sealed record OwnerPayload(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("type")] string Type);

public sealed record InstallationPayload(
    [property: JsonPropertyName("id")] long Id);

public sealed record InstallationTokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record JitConfigResponse(
    [property: JsonPropertyName("encoded_jit_config")] string EncodedJitConfig);

