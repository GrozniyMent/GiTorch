using System.Collections.Concurrent;
using System.Diagnostics;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services.Abstractions;

namespace gitorch.Services;

public sealed class RunnerWorker(
    IJobQueue queue,
    IRunnerEngine engine,
    IRunnerImageService imageService,
    IGitHubService github,
    OrchestratorState state,
    OrchestratorConfig config,
    IProfileResolver profiles,
    OrchestratorMetrics metrics,
    ILogger<RunnerWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _slots = new(config.MaxConcurrentRunners, config.MaxConcurrentRunners);
    private readonly ConcurrentDictionary<long, (CancellationTokenSource Cts, Task Task)> _activeJobs = new();
    private volatile bool _shuttingDown;

    public int AvailableSlots => _slots.CurrentCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await engine.CleanupOrphanedRunnersAsync(stoppingToken);
        await imageService.PullImageAsync(stoppingToken);

        foreach (var pending in state.GetPendingQueuedJobs())
            await queue.EnqueueAsync(pending, stoppingToken);

        try
        {
            await foreach (var job in queue.DequeueAllAsync(stoppingToken))
            {
                if (state.IsTerminal(job.JobId))
                    continue;

                var waitStart = Stopwatch.GetTimestamp();
                await _slots.WaitAsync(stoppingToken);
                metrics.RecordSlotWait(Stopwatch.GetElapsedTime(waitStart).TotalSeconds);
                state.Notify();

                if (state.IsTerminal(job.JobId))
                {
                    _slots.Release();
                    state.Notify();
                    continue;
                }

                var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var task = Task.Run(() => SpawnAndMonitorRunnerAsync(job, jobCts.Token), CancellationToken.None);
                _activeJobs[job.JobId] = (jobCts, task);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on graceful shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _shuttingDown = true;
        queue.Complete();

        await base.StopAsync(cancellationToken);

        var running = _activeJobs.Values.Select(x => x.Task).ToArray();
        if (running.Length == 0)
            return;

        logger.LogInformation("Waiting up to {Seconds}s for {Count} active runner(s) to finish...",
            config.ShutdownTimeoutSeconds, running.Length);

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(config.ShutdownTimeoutSeconds), cancellationToken);
        var allDoneTask = Task.WhenAll(running);

        if (await Task.WhenAny(allDoneTask, timeoutTask) != allDoneTask)
        {
            logger.LogWarning("Graceful shutdown timeout reached; terminating remaining runners.");
            foreach (var entry in _activeJobs.Values)
                entry.Cts.Cancel();

            await Task.WhenAll(running);
        }
    }

    public async Task KillRunnerAsync(long jobId, string containerId)
    {
        state.UpdateStatus(jobId, RunnerStatus.Killed, error: "Terminated via WebUI");
        if (_activeJobs.TryGetValue(jobId, out var active))
            active.Cts.Cancel();
        await engine.ForceRemoveContainerAsync(containerId);
    }

    public async Task CancelJobAsync(long jobId, string reason)
    {
        if (!state.TryGetSession(jobId, out var session) || state.IsTerminal(jobId))
            return;

        state.UpdateStatus(jobId, RunnerStatus.Cancelled, error: reason);

        if (_activeJobs.TryGetValue(jobId, out var active))
            active.Cts.Cancel();

        if (!string.IsNullOrEmpty(session!.ContainerId))
            await engine.ForceRemoveContainerAsync(session.ContainerId);
    }

    private async Task SpawnAndMonitorRunnerAsync(JobRequest job, CancellationToken ct)
    {
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobId"] = job.JobId,
            ["Owner"] = job.Owner,
            ["Repo"] = job.RepoName
        });

        var jobStart = Stopwatch.GetTimestamp();
        var runnerName = $"gh-runner-{job.JobId}-{Guid.NewGuid().ToString("N")[..6]}";
        string? containerId = null;

        try
        {
            var spec = profiles.Resolve(job.Labels);
            state.UpdateStatus(job.JobId, RunnerStatus.Provisioning, runnerName: runnerName);
            var jitConfig = await github.GenerateJitConfigAsync(runnerName, job, ct);

            containerId = await engine.CreateAndStartContainerAsync(runnerName, jitConfig, job, spec, ct);
            state.UpdateStatus(job.JobId, RunnerStatus.Running, runnerName, containerId);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(spec.JobTimeoutMinutes));

            var exitCode = await engine.WaitForExitAsync(containerId, timeoutCts.Token);
            state.CompleteIfNotTerminal(job.JobId, exitCode);
            metrics.IncrementJobExit(exitCode == 0 ? RunnerStatus.Completed : RunnerStatus.Failed, exitCode);
        }
        catch (OperationCanceledException)
        {
            if (!state.IsTerminal(job.JobId))
            {
                var reason = _shuttingDown ? "Orchestrator shutting down" : "Timeout exceeded";
                state.UpdateStatus(job.JobId, RunnerStatus.Killed, error: reason);
                metrics.IncrementJobExit(RunnerStatus.Killed);
            }
            else
            {
                metrics.IncrementJobExit(RunnerStatus.Cancelled);
            }

            if (containerId != null)
                await engine.ForceRemoveContainerAsync(containerId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Runner execution failed for job {JobId}", job.JobId);
            if (!state.IsTerminal(job.JobId))
                state.UpdateStatus(job.JobId, RunnerStatus.Failed, error: ex.Message);

            metrics.IncrementJobExit(RunnerStatus.Failed);
            if (containerId != null)
                await engine.ForceRemoveContainerAsync(containerId);
        }
        finally
        {
            metrics.RecordJobDuration(Stopwatch.GetElapsedTime(jobStart).TotalSeconds);
            if (_activeJobs.TryRemove(job.JobId, out var active))
                active.Cts.Dispose();

            _slots.Release();
            state.Notify();
        }
    }
}
