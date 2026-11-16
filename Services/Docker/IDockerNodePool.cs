using Docker.DotNet;

namespace gitorch.Services.Docker;

public sealed record DockerNodeStatus(string Endpoint, int ActiveContainers);

public interface IDockerNodePool
{
    IReadOnlyList<IDockerClient> AllClients { get; }
    IDockerClient SelectNode();
    void RegisterContainerNode(string containerId, IDockerClient client);
    IDockerClient GetClientForContainer(string containerId);
    void UnregisterContainer(string containerId);
    IReadOnlyList<DockerNodeStatus> GetNodeStatuses();
    string GetNodeEndpoint(IDockerClient client);
}
