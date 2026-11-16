using gitorch.Models;

namespace gitorch.Services.Abstractions;

public interface IRunnerImageService
{
    Task PullImageAsync(CancellationToken ct = default);
}

public interface IRunnerLogReader
{
    IAsyncEnumerable<string> StreamContainerLogsAsync(string containerId, CancellationToken ct = default);
}

public interface IRunnerEngine
{
    Task<string> CreateAndStartContainerAsync(
        string runnerName,
        string jitConfig,
        JobRequest job,
        ResolvedRunnerSpec spec,
        CancellationToken ct);

    Task<long> WaitForExitAsync(string containerId, CancellationToken ct);
    Task CleanupOrphanedRunnersAsync(CancellationToken ct);
    Task ForceRemoveContainerAsync(string containerId);
}

