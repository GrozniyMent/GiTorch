using gitorch.Models;

namespace gitorch.Services.Docker;

internal static class DockerLabels
{
    public const string ManagedByKey = "managed-by";
    public const string ManagedByValue = "runner-orchestrator";

    public static Dictionary<string, string> Build(JobRequest job, string profileName) => new()
    {
        [ManagedByKey] = ManagedByValue,
        ["gh-owner"] = job.Owner,
        ["gh-repo"] = job.RepoName,
        ["gh-job-id"] = job.JobId.ToString(),
        ["gh-profile"] = profileName
    };
}

