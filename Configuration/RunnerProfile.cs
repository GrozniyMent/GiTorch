namespace gitorch.Configuration;

public sealed class RunnerProfile
{
    public string Name { get; set; } = "";
    public string[] MatchLabels { get; set; } = [];
    public string? RunnerImage { get; set; }
    public long? MemoryLimitMb { get; set; }
    public double? CpuLimitCores { get; set; }
    public int? JobTimeoutMinutes { get; set; }
    public long? PidsLimit { get; set; }
    public bool? UseDinDSidecar { get; set; }
    public bool EnableGpu { get; set; } = false;
}

