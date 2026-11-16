using Docker.DotNet;
using Docker.DotNet.Models;
using gitorch.Configuration;

namespace gitorch.Services.Docker;

public sealed class DockerNetworkService(OrchestratorConfig config)
{
    public async Task EnsureRunnerNetworkAsync(IDockerClient docker, CancellationToken ct)
    {
        var networks = await docker.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [config.RunnerNetworkName] = true }
            }
        }, ct);

        if (networks.Any(n => n.Name == config.RunnerNetworkName))
            return;

        await docker.Networks.CreateNetworkAsync(new NetworksCreateParameters
        {
            Name = config.RunnerNetworkName,
            Driver = "bridge",
            CheckDuplicate = true
        }, ct);
    }
}
