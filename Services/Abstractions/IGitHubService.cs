using gitorch.Models;

namespace gitorch.Services.Abstractions;

public interface IGitHubService
{
    Task<string> GenerateJitConfigAsync(string runnerName, JobRequest job, CancellationToken ct);
}

