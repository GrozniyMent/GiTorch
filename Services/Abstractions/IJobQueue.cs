using gitorch.Models;

namespace gitorch.Services.Abstractions;

public interface IJobQueue
{
    ValueTask<bool> EnqueueAsync(JobRequest job, CancellationToken ct = default);
    IAsyncEnumerable<JobRequest> DequeueAllAsync(CancellationToken ct);
    void Complete();
}

