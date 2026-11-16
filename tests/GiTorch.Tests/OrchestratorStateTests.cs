using gitorch.Models;
using gitorch.Services;
using gitorch.Services.Abstractions;
using Xunit;

namespace GiTorch.Tests;

public sealed class OrchestratorStateTests
{
    private sealed class InMemorySessionRepository : ISessionRepository
    {
        private readonly HashSet<string> _deliveries = [];
        public readonly Dictionary<long, RunnerSession> Sessions = [];

        public bool TryRecordDelivery(string deliveryId) => _deliveries.Add(deliveryId);

        public void UpsertSession(RunnerSession session) => Sessions[session.JobId] = session;

        public void MarkInterruptedSessionsAsFailed() { }

        public IReadOnlyList<RunnerSession> LoadRecentSessions(int maxCompleted) =>
            Sessions.Values.ToList();

        public int PurgeExpiredRecords(TimeSpan deliveryRetention, TimeSpan sessionRetention) => 0;
    }

    private static JobRequest SampleJob(long jobId = 101) =>
        new(jobId, ["self-hosted", "linux"], 99, "acme", "api", IsOrganization: false);

    [Fact]
    public void TryRegisterQueued_DeduplicatesByDeliveryIdAndJobId()
    {
        var repo = new InMemorySessionRepository();
        var state = new OrchestratorState(repo);

        Assert.True(state.TryRegisterQueued(SampleJob(101), "del-1"));
        Assert.False(state.TryRegisterQueued(SampleJob(102), "del-1")); // duplicate delivery
        Assert.False(state.TryRegisterQueued(SampleJob(101), "del-2")); // duplicate job id
        Assert.Single(state.GetSessions());
    }

    [Fact]
    public void UpdateStatus_SetsLifecycleTimestamps()
    {
        var state = new OrchestratorState(new InMemorySessionRepository());
        state.TryRegisterQueued(SampleJob(201), "del-201");

        state.UpdateStatus(201, RunnerStatus.Provisioning, runnerName: "runner-1");
        Assert.True(state.TryGetSession(201, out var session));
        Assert.Equal("runner-1", session!.RunnerName);
        Assert.Null(session.StartedAt);

        state.UpdateStatus(201, RunnerStatus.Running, containerId: "cid-123");
        Assert.NotNull(session.StartedAt);
        Assert.Null(session.FinishedAt);

        state.CompleteIfNotTerminal(201, exitCode: 0);
        Assert.Equal(RunnerStatus.Completed, session.Status);
        Assert.NotNull(session.FinishedAt);
    }

    [Fact]
    public void CompleteIfNotTerminal_DoesNotOverwriteKilledOrCancelled()
    {
        var state = new OrchestratorState(new InMemorySessionRepository());
        state.TryRegisterQueued(SampleJob(301), "del-301");
        state.UpdateStatus(301, RunnerStatus.Killed, error: "Terminated via WebUI");

        state.CompleteIfNotTerminal(301, exitCode: 0);

        Assert.True(state.TryGetSession(301, out var session));
        Assert.Equal(RunnerStatus.Killed, session!.Status);
    }
}

