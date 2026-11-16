using System.Collections.Concurrent;
using Docker.DotNet;
using Docker.DotNet.Models;
using gitorch.Configuration;
using gitorch.Models;

namespace gitorch.Services.Docker;

public sealed class DinDSidecarManager(OrchestratorConfig config)
{
    private readonly ConcurrentDictionary<string, (IDockerClient Client, string SidecarId)> _sidecars = new();

    public async Task<string?> StartIfEnabledAsync(
        IDockerClient docker,
        string runnerName,
        JobRequest job,
        ResolvedRunnerSpec spec,
        CancellationToken ct)
    {
        if (!spec.UseDinDSidecar)
            return null;

        var sidecarName = $"{runnerName}-dind";
        var dindParams = new CreateContainerParameters
        {
            Image = config.DinDImage,
            Name = sidecarName,
            Env = ["DOCKER_TLS_CERTDIR="],
            Labels = DockerLabels.Build(job, spec.ProfileName),
            HostConfig = new HostConfig
            {
                AutoRemove = true,
                Privileged = true,
                NetworkMode = config.RunnerNetworkName,
                Memory = spec.MemoryLimitMb * 1024 * 1024,
                NanoCPUs = (long)(spec.CpuLimitCores * 1_000_000_000)
            }
        };

        var resp = await docker.Containers.CreateContainerAsync(dindParams, ct);
        await docker.Containers.StartContainerAsync(resp.ID, new ContainerStartParameters(), ct);
        return resp.ID;
    }

    public void AttachToRunner(string runnerContainerId, IDockerClient client, string sidecarId) =>
        _sidecars[runnerContainerId] = (client, sidecarId);

    public async Task RemoveForRunnerAsync(string runnerContainerId)
    {
        if (_sidecars.TryRemove(runnerContainerId, out var entry))
            await RemoveContainerSafeAsync(entry.Client, entry.SidecarId);
    }

    public async Task RemoveContainerSafeAsync(IDockerClient docker, string containerId)
    {
        try
        {
            await docker.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true });
        }
        catch
        {
            // Ignore errors if container was already auto-removed.
        }
    }
}
