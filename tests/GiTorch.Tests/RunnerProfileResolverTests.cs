using gitorch.Configuration;
using gitorch.Services;
using Xunit;

namespace GiTorch.Tests;

public sealed class RunnerProfileResolverTests
{
    [Fact]
    public void Resolve_ReturnsDefaultsWhenNoProfileMatches()
    {
        var config = new OrchestratorConfig
        {
            RunnerImage = "default-img:1.0",
            MemoryLimitMb = 2048,
            CpuLimitCores = 2.0,
            JobTimeoutMinutes = 30,
            PidsLimit = 256,
            Profiles =
            [
                new RunnerProfile
                {
                    Name = "gpu",
                    MatchLabels = ["self-hosted", "gpu"],
                    RunnerImage = "gpu-img:latest",
                    EnableGpu = true
                }
            ]
        };

        var resolver = new RunnerProfileResolver(config);
        var spec = resolver.Resolve(["self-hosted", "linux"]);

        Assert.Equal("default", spec.ProfileName);
        Assert.Equal("default-img:1.0", spec.RunnerImage);
        Assert.Equal(2048, spec.MemoryLimitMb);
        Assert.False(spec.EnableGpu);
    }

    [Fact]
    public void Resolve_AppliesMatchedProfileOverrides()
    {
        var config = new OrchestratorConfig
        {
            RunnerImage = "default-img:1.0",
            MemoryLimitMb = 2048,
            CpuLimitCores = 2.0,
            JobTimeoutMinutes = 30,
            Profiles =
            [
                new RunnerProfile
                {
                    Name = "gpu-xl",
                    MatchLabels = ["self-hosted", "gpu"],
                    RunnerImage = "gpu-img:2.0",
                    MemoryLimitMb = 16384,
                    CpuLimitCores = 8.0,
                    JobTimeoutMinutes = 120,
                    UseDinDSidecar = true,
                    EnableGpu = true
                }
            ]
        };

        var resolver = new RunnerProfileResolver(config);
        var spec = resolver.Resolve(["GPU", "Self-Hosted", "x64"]);

        Assert.Equal("gpu-xl", spec.ProfileName);
        Assert.Equal("gpu-img:2.0", spec.RunnerImage);
        Assert.Equal(16384, spec.MemoryLimitMb);
        Assert.Equal(8.0, spec.CpuLimitCores);
        Assert.Equal(120, spec.JobTimeoutMinutes);
        Assert.True(spec.UseDinDSidecar);
        Assert.True(spec.EnableGpu);
    }
}

