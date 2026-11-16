using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services.Abstractions;

namespace gitorch.Services.Docker;

public sealed class DockerRunnerEngine(
    IDockerNodePool nodePool,
    DockerNetworkService networkService,
    DinDSidecarManager sidecarManager,
    IEnumerable<IRunnerContainerCustomizer> customizers,
    OrchestratorState state,
    OrchestratorConfig config,
    ILogger<DockerRunnerEngine> logger) : IRunnerEngine, IRunnerLogReader
{
    public async Task<string> CreateAndStartContainerAsync(
        string runnerName,
        string jitConfig,
        JobRequest job,
        ResolvedRunnerSpec spec,
        CancellationToken ct)
    {
        var docker = nodePool.SelectNode();
        state.SetExecutionContext(job.JobId, spec, nodePool.GetNodeEndpoint(docker));
        await networkService.EnsureRunnerNetworkAsync(docker, ct);

        var env = new List<string>();
        var sidecarId = await sidecarManager.StartIfEnabledAsync(docker, runnerName, job, spec, ct);
        if (sidecarId != null)
            env.Add($"DOCKER_HOST=tcp://{runnerName}-dind:2375");

        var createParams = new CreateContainerParameters
        {
            Image = spec.RunnerImage,
            Name = runnerName,
            Cmd = ["./run.sh", "--jitconfig", jitConfig],
            Env = env,
            Labels = DockerLabels.Build(job, spec.ProfileName),
            HostConfig = new HostConfig
            {
                AutoRemove = true,
                Binds = [],
                NetworkMode = config.RunnerNetworkName,
                Memory = spec.MemoryLimitMb * 1024 * 1024,
                NanoCPUs = (long)(spec.CpuLimitCores * 1_000_000_000),
                PidsLimit = spec.PidsLimit,
                CapDrop = ["ALL"],
                SecurityOpt = ["no-new-privileges:true"]
            }
        };

        foreach (var customizer in customizers)
            customizer.Customize(createParams, job, spec);

        try
        {
            var response = await docker.Containers.CreateContainerAsync(createParams, ct);
            nodePool.RegisterContainerNode(response.ID, docker);
            if (sidecarId != null)
                sidecarManager.AttachToRunner(response.ID, docker, sidecarId);

            await docker.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);
            return response.ID;
        }
        catch
        {
            if (sidecarId != null)
                await sidecarManager.RemoveContainerSafeAsync(docker, sidecarId);
            throw;
        }
    }

    public async Task<long> WaitForExitAsync(string containerId, CancellationToken ct)
    {
        var docker = nodePool.GetClientForContainer(containerId);
        try
        {
            var response = await docker.Containers.WaitContainerAsync(containerId, ct);
            return response.StatusCode;
        }
        finally
        {
            await sidecarManager.RemoveForRunnerAsync(containerId);
            nodePool.UnregisterContainer(containerId);
        }
    }

    public async IAsyncEnumerable<string> StreamContainerLogsAsync(
        string containerId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var docker = nodePool.GetClientForContainer(containerId);
        MultiplexedStream? stream = null;
        string? initError = null;

        try
        {
            stream = await docker.Containers.GetContainerLogsAsync(
                containerId,
                tty: false,
                new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Follow = true,
                    Tail = "150",
                    Timestamps = true
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (Exception ex)
        {
            initError = $"[Container already removed or logs unavailable: {ex.Message}]\n";
        }

        if (initError != null)
        {
            yield return initError;
            yield break;
        }

        using (stream)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    MultiplexedStream.ReadResult result = default;
                    string? streamError = null;
                    try
                    {
                        result = await stream!.ReadOutputAsync(buffer, 0, buffer.Length, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        streamError = $"\n[Log stream ended: {ex.Message}]\n";
                    }

                    if (streamError != null)
                    {
                        yield return streamError;
                        yield break;
                    }

                    if (result.EOF)
                        yield break;

                    if (result.Count > 0)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        yield return result.Target == MultiplexedStream.TargetStream.StandardError
                            ? $"[STDERR] {text}"
                            : text;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public async Task CleanupOrphanedRunnersAsync(CancellationToken ct)
    {
        foreach (var docker in nodePool.AllClients)
        {
            try
            {
                var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["label"] = new Dictionary<string, bool> { [$"{DockerLabels.ManagedByKey}={DockerLabels.ManagedByValue}"] = true }
                    }
                }, ct);

                foreach (var container in containers)
                    await sidecarManager.RemoveContainerSafeAsync(docker, container.ID);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not cleanup old containers on startup");
            }
        }
    }

    public async Task ForceRemoveContainerAsync(string containerId)
    {
        var docker = nodePool.GetClientForContainer(containerId);
        await sidecarManager.RemoveContainerSafeAsync(docker, containerId);
        await sidecarManager.RemoveForRunnerAsync(containerId);
        nodePool.UnregisterContainer(containerId);
    }
}
