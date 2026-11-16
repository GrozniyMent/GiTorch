namespace gitorch.Models;

public static class RunnerStatus
{
    public const string Queued = "QUEUED";
    public const string Provisioning = "PROVISIONING";
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Killed = "KILLED";
    public const string Cancelled = "CANCELLED";
}

public sealed record JobRequest(
    long JobId,
    string[] Labels,
    long InstallationId,
    string Owner,
    string RepoName,
    bool IsOrganization);

public sealed class RunnerSession
{
    public required long JobId { get; init; }
    public required long InstallationId { get; init; }
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public required bool IsOrg { get; init; }
    public required string[] Labels { get; init; }
    public string RunnerName { get; set; } = "pending...";
    public string? ContainerId { get; set; }
    public string Status { get; set; } = RunnerStatus.Queued;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset QueuedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string ProfileName { get; set; } = "default";
    public string NodeEndpoint { get; set; } = "";
    public double CpuCores { get; set; }
    public long MemoryMb { get; set; }
    public bool GpuEnabled { get; set; }
    public bool UseDinD { get; set; }
}

