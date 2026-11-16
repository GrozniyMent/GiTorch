using System.Collections.Concurrent;
using Docker.DotNet;
using gitorch.Configuration;

namespace gitorch.Services.Docker;

public sealed class RoundRobinDockerNodePool : IDockerNodePool
{
    private readonly IReadOnlyList<(string Endpoint, IDockerClient Client)> _nodes;
    private readonly IReadOnlyList<IDockerClient> _clients;
    private readonly ConcurrentDictionary<string, IDockerClient> _containerNodes = new();
    private int _nextIndex = -1;

    public RoundRobinDockerNodePool(OrchestratorConfig config)
    {
        var uris = config.DockerNodes.Count > 0
            ? config.DockerNodes
            : [config.DockerSocket];

        _nodes = uris
            .Select(uri => (uri, (IDockerClient)new DockerClientConfiguration(new Uri(uri)).CreateClient()))
            .ToList();
        _clients = _nodes.Select(n => n.Client).ToList();
    }

    public RoundRobinDockerNodePool(IDockerClient singleClient)
    {
        _nodes = [("local", singleClient)];
        _clients = [singleClient];
    }

    public IReadOnlyList<IDockerClient> AllClients => _clients;

    public IDockerClient SelectNode()
    {
        var idx = (uint)Interlocked.Increment(ref _nextIndex) % (uint)_clients.Count;
        return _clients[(int)idx];
    }

    public void RegisterContainerNode(string containerId, IDockerClient client) =>
        _containerNodes[containerId] = client;

    public IDockerClient GetClientForContainer(string containerId) =>
        _containerNodes.TryGetValue(containerId, out var client) ? client : _clients[0];

    public void UnregisterContainer(string containerId) =>
        _containerNodes.TryRemove(containerId, out _);

    public string GetNodeEndpoint(IDockerClient client)
    {
        foreach (var node in _nodes)
        {
            if (ReferenceEquals(node.Client, client))
                return node.Endpoint;
        }
        return "local";
    }

    public IReadOnlyList<DockerNodeStatus> GetNodeStatuses()
    {
        var counts = new Dictionary<IDockerClient, int>(ReferenceEqualityComparer.Instance);
        foreach (var client in _containerNodes.Values)
            counts[client] = counts.GetValueOrDefault(client) + 1;

        return _nodes
            .Select(n => new DockerNodeStatus(n.Endpoint, counts.GetValueOrDefault(n.Client)))
            .ToList();
    }
}
