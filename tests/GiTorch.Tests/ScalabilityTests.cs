using Docker.DotNet;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services;
using gitorch.Services.Docker;
using Xunit;

namespace GiTorch.Tests;

public sealed class ScalabilityTests
{
    [Fact]
    public void RoundRobinDockerNodePool_RotatesNodesAndTracksContainers()
    {
        var config = new OrchestratorConfig
        {
            DockerNodes = ["http://node-1:2375", "http://node-2:2375"]
        };

        var pool = new RoundRobinDockerNodePool(config);
        Assert.Equal(2, pool.AllClients.Count);

        var first = pool.SelectNode();
        var second = pool.SelectNode();
        var third = pool.SelectNode();

        Assert.NotSame(first, second);
        Assert.Same(first, third);

        pool.RegisterContainerNode("cid-node-2", second);
        Assert.Same(second, pool.GetClientForContainer("cid-node-2"));
        Assert.Equal("http://node-2:2375", pool.GetNodeEndpoint(second));

        var statuses = pool.GetNodeStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal(0, statuses.Single(s => s.Endpoint == "http://node-1:2375").ActiveContainers);
        Assert.Equal(1, statuses.Single(s => s.Endpoint == "http://node-2:2375").ActiveContainers);

        pool.UnregisterContainer("cid-node-2");
        Assert.Same(pool.AllClients[0], pool.GetClientForContainer("cid-node-2"));
        Assert.Equal(0, pool.GetNodeStatuses().Sum(s => s.ActiveContainers));
    }

    [Fact]
    public void SessionRepository_PurgeExpiredRecords_DeletesOldDeliveriesAndFinishedSessions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gitorch-ret-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SessionRepository(new OrchestratorConfig { DatabasePath = dbPath });

            Assert.True(repo.TryRecordDelivery("old-del"));

            var oldFinished = new RunnerSession
            {
                JobId = 1,
                InstallationId = 10,
                Owner = "acme",
                Repo = "old",
                IsOrg = false,
                Labels = ["self-hosted"],
                Status = RunnerStatus.Completed,
                QueuedAt = DateTimeOffset.UtcNow.AddDays(-60),
                FinishedAt = DateTimeOffset.UtcNow.AddDays(-40)
            };
            var active = new RunnerSession
            {
                JobId = 2,
                InstallationId = 10,
                Owner = "acme",
                Repo = "active",
                IsOrg = false,
                Labels = ["self-hosted"],
                Status = RunnerStatus.Running,
                QueuedAt = DateTimeOffset.UtcNow.AddDays(-40)
            };

            repo.UpsertSession(oldFinished);
            repo.UpsertSession(active);

            var deleted = repo.PurgeExpiredRecords(TimeSpan.FromDays(7), TimeSpan.FromDays(30));
            Assert.Equal(1, deleted); // oldFinished deleted, active preserved

            var loaded = repo.LoadRecentSessions(30);
            Assert.Single(loaded);
            Assert.Equal(2, loaded[0].JobId);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}

