using System.Collections.Concurrent;
using gitorch.Models;
using gitorch.Services.Abstractions;

namespace gitorch.Services;

public sealed class OrchestratorState
{
    private const int MaxHistoryCount = 30;
    private readonly ConcurrentDictionary<long, RunnerSession> _sessions = new();
    private readonly ISessionRepository _repository;
    private volatile IReadOnlyList<RunnerSession> _cachedSnapshot = [];

    public OrchestratorState(ISessionRepository repository)
    {
        _repository = repository;
        _repository.MarkInterruptedSessionsAsFailed();

        foreach (var session in _repository.LoadRecentSessions(MaxHistoryCount))
            _sessions[session.JobId] = session;

        RebuildSnapshot();
    }

    public string ImageStatus { get; private set; } = "IDLE";
    public event Action? OnChange;

    public IReadOnlyList<RunnerSession> GetSessions() => _cachedSnapshot;

    public IReadOnlyList<JobRequest> GetPendingQueuedJobs() =>
        _sessions.Values
            .Where(x => x.Status == RunnerStatus.Queued)
            .OrderBy(x => x.QueuedAt)
            .Select(x => new JobRequest(x.JobId, x.Labels, x.InstallationId, x.Owner, x.Repo, x.IsOrg))
            .ToList();

    public void SetImageStatus(string status)
    {
        ImageStatus = status;
        Notify();
    }

    public void SetExecutionContext(long jobId, ResolvedRunnerSpec spec, string nodeEndpoint)
    {
        if (!_sessions.TryGetValue(jobId, out var session))
            return;

        session.ProfileName = spec.ProfileName;
        session.NodeEndpoint = nodeEndpoint;
        session.CpuCores = spec.CpuLimitCores;
        session.MemoryMb = spec.MemoryLimitMb;
        session.GpuEnabled = spec.EnableGpu;
        session.UseDinD = spec.UseDinDSidecar;
        Notify();
    }

    public bool TryRegisterQueued(JobRequest req, string? deliveryId)
    {
        if (!string.IsNullOrWhiteSpace(deliveryId) && !_repository.TryRecordDelivery(deliveryId))
            return false;

        var session = new RunnerSession
        {
            JobId = req.JobId,
            InstallationId = req.InstallationId,
            Owner = req.Owner,
            Repo = req.RepoName,
            IsOrg = req.IsOrganization,
            Labels = req.Labels,
            Status = RunnerStatus.Queued
        };

        if (!_sessions.TryAdd(req.JobId, session))
            return false;

        _repository.UpsertSession(session);
        TrimHistory();
        Notify();
        return true;
    }

    public bool IsTerminal(long jobId) =>
        !_sessions.TryGetValue(jobId, out var session) ||
        session.Status is RunnerStatus.Completed or RunnerStatus.Failed or RunnerStatus.Killed or RunnerStatus.Cancelled;

    public bool TryGetSession(long jobId, out RunnerSession? session) =>
        _sessions.TryGetValue(jobId, out session);

    public void UpdateStatus(
        long jobId,
        string status,
        string? runnerName = null,
        string? containerId = null,
        string? error = null)
    {
        if (!_sessions.TryGetValue(jobId, out var session))
            return;

        session.Status = status;
        if (runnerName != null) session.RunnerName = runnerName;
        if (containerId != null) session.ContainerId = containerId;
        if (error != null) session.ErrorMessage = error;

        if (status == RunnerStatus.Running)
            session.StartedAt = DateTimeOffset.UtcNow;
        else if (status is RunnerStatus.Completed or RunnerStatus.Failed or RunnerStatus.Killed or RunnerStatus.Cancelled)
            session.FinishedAt = DateTimeOffset.UtcNow;

        _repository.UpsertSession(session);
        Notify();
    }

    public void CompleteIfNotTerminal(long jobId, long exitCode)
    {
        if (IsTerminal(jobId))
            return;

        UpdateStatus(
            jobId,
            exitCode == 0 ? RunnerStatus.Completed : RunnerStatus.Failed,
            error: exitCode != 0 ? $"Exit code: {exitCode}" : null);
    }

    public void Notify()
    {
        RebuildSnapshot();
        OnChange?.Invoke();
    }

    private void RebuildSnapshot() =>
        _cachedSnapshot = _sessions.Values.OrderByDescending(x => x.QueuedAt).ToArray();

    private void TrimHistory()
    {
        var expired = _sessions.Values
            .Where(x => x.FinishedAt != null)
            .OrderByDescending(x => x.FinishedAt)
            .Skip(MaxHistoryCount)
            .ToList();

        foreach (var old in expired)
            _sessions.TryRemove(old.JobId, out _);
    }
}
