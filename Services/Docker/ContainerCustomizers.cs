using System.Text.RegularExpressions;
using Docker.DotNet.Models;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services.Abstractions;

namespace gitorch.Services.Docker;

public sealed class HostSocketCustomizer(OrchestratorConfig config) : IRunnerContainerCustomizer
{
    public void Customize(CreateContainerParameters parameters, JobRequest job, ResolvedRunnerSpec spec)
    {
        if (!config.MountDockerSocket)
            return;

        parameters.HostConfig.Binds ??= [];
        parameters.HostConfig.Binds.Add("/var/run/docker.sock:/var/run/docker.sock");
    }
}

public sealed class GpuDeviceCustomizer : IRunnerContainerCustomizer
{
    public void Customize(CreateContainerParameters parameters, JobRequest job, ResolvedRunnerSpec spec)
    {
        if (!spec.EnableGpu)
            return;

        parameters.HostConfig.DeviceRequests =
        [
            new DeviceRequest
            {
                Driver = "nvidia",
                Count = -1,
                Capabilities = [["gpu"]]
            }
        ];
    }
}

public sealed partial class RepoCacheCustomizer(OrchestratorConfig config) : IRunnerContainerCustomizer
{
    public void Customize(CreateContainerParameters parameters, JobRequest job, ResolvedRunnerSpec spec)
    {
        if (!config.EnableRepoCache)
            return;

        var volumeName = $"{Sanitize(config.CacheVolumePrefix)}-{Sanitize(job.Owner)}-{Sanitize(job.RepoName)}";

        parameters.HostConfig.Binds ??= [];
        parameters.HostConfig.Binds.Add($"{volumeName}:{config.CacheMountPath}");

        parameters.Env ??= [];
        parameters.Env.Add($"GITORCH_CACHE_DIR={config.CacheMountPath}");
    }

    private static string Sanitize(string value)
    {
        var cleaned = InvalidVolumeCharsRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(cleaned) ? "default" : cleaned;
    }

    [GeneratedRegex("[^a-z0-9_.-]+")]
    private static partial Regex InvalidVolumeCharsRegex();
}

