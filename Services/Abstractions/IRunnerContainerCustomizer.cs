using Docker.DotNet.Models;
using gitorch.Models;

namespace gitorch.Services.Abstractions;

public interface IRunnerContainerCustomizer
{
    void Customize(CreateContainerParameters parameters, JobRequest job, ResolvedRunnerSpec spec);
}

