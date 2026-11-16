using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using gitorch.Configuration;
using gitorch.Models;

namespace gitorch.Services;

public sealed class OrchestratorMetrics
{
    private long _githubApiErrors;
    private long _slotWaitCount;
    private double _slotWaitSumSeconds;
    private long _jobDurationCount;
    private double _jobDurationSumSeconds;
    private readonly Lock _durationLock = new();
    private readonly ConcurrentDictionary<(string Status, string ExitCode), long> _jobExits = new();

    public long GitHubApiErrors => Interlocked.Read(ref _githubApiErrors);

    public double AverageSlotWaitSeconds
    {
        get { lock (_durationLock) return _slotWaitCount > 0 ? _slotWaitSumSeconds / _slotWaitCount : 0; }
    }

    public double AverageJobDurationSeconds
    {
        get { lock (_durationLock) return _jobDurationCount > 0 ? _jobDurationSumSeconds / _jobDurationCount : 0; }
    }

    public void IncrementGitHubApiError() => Interlocked.Increment(ref _githubApiErrors);

    public void RecordSlotWait(double seconds)
    {
        lock (_durationLock)
        {
            _slotWaitCount++;
            _slotWaitSumSeconds += seconds;
        }
    }

    public void RecordJobDuration(double seconds)
    {
        lock (_durationLock)
        {
            _jobDurationCount++;
            _jobDurationSumSeconds += seconds;
        }
    }

    public void IncrementJobExit(string status, long? exitCode = null)
    {
        var code = exitCode?.ToString(CultureInfo.InvariantCulture) ?? "none";
        _jobExits.AddOrUpdate((status, code), 1, (_, current) => current + 1);
    }

    public string RenderPrometheus(OrchestratorState state, RunnerWorker worker, OrchestratorConfig config)
    {
        var sessions = state.GetSessions();
        var queued = sessions.Count(s => s.Status is RunnerStatus.Queued or RunnerStatus.Provisioning);
        var active = config.MaxConcurrentRunners - worker.AvailableSlots;

        long waitCount, durCount;
        double waitSum, durSum;
        lock (_durationLock)
        {
            waitCount = _slotWaitCount;
            waitSum = _slotWaitSumSeconds;
            durCount = _jobDurationCount;
            durSum = _jobDurationSumSeconds;
        }

        var sb = new StringBuilder();
        AppendMetric(sb, "gitorch_queued_jobs", "gauge", "Number of queued or provisioning jobs", queued);
        AppendMetric(sb, "gitorch_active_runners", "gauge", "Number of currently active runner slots", active);
        AppendMetric(sb, "gitorch_available_slots", "gauge", "Number of available runner slots", worker.AvailableSlots);
        AppendMetric(sb, "gitorch_github_api_errors_total", "counter", "Total GitHub API errors",
            Interlocked.Read(ref _githubApiErrors));

        sb.AppendLine("# HELP gitorch_slot_wait_seconds Time spent waiting for a runner slot");
        sb.AppendLine("# TYPE gitorch_slot_wait_seconds summary");
        sb.AppendLine(FormattableString.Invariant($"gitorch_slot_wait_seconds_sum {waitSum:F6}"));
        sb.AppendLine(FormattableString.Invariant($"gitorch_slot_wait_seconds_count {waitCount}"));

        sb.AppendLine("# HELP gitorch_job_duration_seconds Duration of executed runner jobs");
        sb.AppendLine("# TYPE gitorch_job_duration_seconds summary");
        sb.AppendLine(FormattableString.Invariant($"gitorch_job_duration_seconds_sum {durSum:F6}"));
        sb.AppendLine(FormattableString.Invariant($"gitorch_job_duration_seconds_count {durCount}"));

        sb.AppendLine("# HELP gitorch_job_exits_total Completed jobs by status and exit code");
        sb.AppendLine("# TYPE gitorch_job_exits_total counter");
        foreach (var ((status, code), count) in _jobExits.OrderBy(x => x.Key.Status).ThenBy(x => x.Key.ExitCode))
        {
            sb.AppendLine(FormattableString.Invariant(
                $"gitorch_job_exits_total{{status=\"{status}\",exit_code=\"{code}\"}} {count}"));
        }

        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, string name, string type, string help, long value)
    {
        sb.AppendLine($"# HELP {name} {help}");
        sb.AppendLine($"# TYPE {name} {type}");
        sb.AppendLine(FormattableString.Invariant($"{name} {value}"));
    }
}

