namespace gitorch.Services.Abstractions;

public interface IProfileResolver
{
    ResolvedRunnerSpec Resolve(string[] jobLabels);
    IReadOnlyList<string> GetAllConfiguredImages();
}

