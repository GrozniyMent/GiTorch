using Docker.DotNet;
using Docker.DotNet.Models;
using gitorch.Configuration;
using gitorch.Services.Abstractions;

namespace gitorch.Services.Docker;

public sealed class DockerImageService(
    IDockerNodePool nodePool,
    OrchestratorState state,
    OrchestratorConfig config,
    IProfileResolver profiles,
    ILogger<DockerImageService> logger) : IRunnerImageService
{
    public async Task PullImageAsync(CancellationToken ct = default)
    {
        var auth = BuildRegistryAuthConfig();
        var images = profiles.GetAllConfiguredImages();

        foreach (var docker in nodePool.AllClients)
        {
            for (var i = 0; i < images.Count; i++)
                await PullSingleImageAsync(docker, images[i], auth, updateUiStatus: i == 0, ct);

            if (config.UseDinDSidecar || config.Profiles.Any(p => p.UseDinDSidecar == true))
                await PullSingleImageAsync(docker, config.DinDImage, authConfig: null, updateUiStatus: false, ct);
        }
    }

    private async Task PullSingleImageAsync(
        IDockerClient docker,
        string image,
        AuthConfig? authConfig,
        bool updateUiStatus,
        CancellationToken ct)
    {
        try
        {
            if (updateUiStatus)
                state.SetImageStatus("PULLING...");

            var (fromImage, tag) = SplitImageTag(image);

            await docker.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = fromImage, Tag = tag },
                authConfig,
                new Progress<JSONMessage>(m =>
                {
                    if (updateUiStatus && !string.IsNullOrEmpty(m.Status))
                        state.SetImageStatus(m.Status.ToUpperInvariant());
                }),
                ct);

            if (updateUiStatus)
                state.SetImageStatus("READY");
        }
        catch (Exception ex)
        {
            if (updateUiStatus)
                state.SetImageStatus("PULL FAILED (CACHED?)");
            logger.LogWarning(ex, "Could not pull image {Image}", image);
        }
    }

    private AuthConfig? BuildRegistryAuthConfig()
    {
        if (string.IsNullOrWhiteSpace(config.RegistryUsername) || string.IsNullOrWhiteSpace(config.RegistryPassword))
            return null;

        var server = config.RegistryServer;
        if (string.IsNullOrWhiteSpace(server))
        {
            var firstSegment = config.RunnerImage.Split('/', 2)[0];
            if (firstSegment.Contains('.') || firstSegment.Contains(':'))
                server = firstSegment;
        }

        return new AuthConfig
        {
            Username = config.RegistryUsername,
            Password = config.RegistryPassword,
            ServerAddress = server
        };
    }

    private static (string FromImage, string Tag) SplitImageTag(string image)
    {
        var lastSlash = image.LastIndexOf('/');
        var lastColon = image.LastIndexOf(':');
        if (lastColon > lastSlash)
            return (image[..lastColon], image[(lastColon + 1)..]);

        return (image, "latest");
    }
}

