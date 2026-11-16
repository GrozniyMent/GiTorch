using gitorch.Configuration;
using gitorch.Services.Abstractions;

namespace gitorch.Services;

public sealed record ResolvedRunnerSpec(
    string ProfileName,
    string RunnerImage,
    long MemoryLimitMb,
    double CpuLimitCores,
    int JobTimeoutMinutes,
    long PidsLimit,
    bool UseDinDSidecar,
    bool EnableGpu);

public sealed class RunnerProfileResolver(OrchestratorConfig config) : IProfileResolver
{
    public ResolvedRunnerSpec Resolve(string[] jobLabels)
    {
        var labelSet = new HashSet<string>(jobLabels, StringComparer.OrdinalIgnoreCase);
        var matched = config.Profiles.FirstOrDefault(p =>
            p.MatchLabels.Length > 0 && p.MatchLabels.All(labelSet.Contains));

        return new ResolvedRunnerSpec(
            ProfileName: matched?.Name ?? "default",
            RunnerImage: string.IsNullOrWhiteSpace(matched?.RunnerImage) ? config.RunnerImage : matched.RunnerImage,
            MemoryLimitMb: matched?.MemoryLimitMb ?? config.MemoryLimitMb,
            CpuLimitCores: matched?.CpuLimitCores ?? config.CpuLimitCores,
            JobTimeoutMinutes: matched?.JobTimeoutMinutes ?? config.JobTimeoutMinutes,
            PidsLimit: matched?.PidsLimit ?? config.PidsLimit,
            UseDinDSidecar: matched?.UseDinDSidecar ?? config.UseDinDSidecar,
            EnableGpu: matched?.EnableGpu ?? false);
    }

    public IReadOnlyList<string> GetAllConfiguredImages() =>
        config.Profiles
            .Select(p => p.RunnerImage)
            .Where(img => !string.IsNullOrWhiteSpace(img))
            .Prepend(config.RunnerImage)
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();
}

