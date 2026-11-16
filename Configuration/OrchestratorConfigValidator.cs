using Microsoft.Extensions.Options;

namespace gitorch.Configuration;

public sealed class OrchestratorConfigValidator : IValidateOptions<OrchestratorConfig>
{
    public ValidateOptionsResult Validate(string? name, OrchestratorConfig options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AppId))
            errors.Add("Orchestrator:AppId is required.");

        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
            errors.Add("Orchestrator:WebhookSecret is required.");

        if (string.IsNullOrWhiteSpace(options.PrivateKeyPathOrPem))
        {
            errors.Add("Orchestrator:PrivateKeyPathOrPem is required.");
        }
        else if (!File.Exists(options.PrivateKeyPathOrPem) &&
                 !options.PrivateKeyPathOrPem.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            errors.Add($"Orchestrator:PrivateKeyPathOrPem file not found and is not an inline PEM: '{options.PrivateKeyPathOrPem}'.");
        }

        if (!Uri.TryCreate(options.DockerSocket, UriKind.Absolute, out var dockerUri))
        {
            errors.Add($"Orchestrator:DockerSocket is not a valid URI: '{options.DockerSocket}'.");
        }
        else if (dockerUri.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase) &&
                 !File.Exists(dockerUri.AbsolutePath))
        {
            errors.Add($"Docker socket does not exist at '{dockerUri.AbsolutePath}'.");
        }

        if (options.MaxConcurrentRunners <= 0)
            errors.Add("Orchestrator:MaxConcurrentRunners must be greater than 0.");

        if (options.MemoryLimitMb <= 0)
            errors.Add("Orchestrator:MemoryLimitMb must be greater than 0.");

        if (options.CpuLimitCores <= 0)
            errors.Add("Orchestrator:CpuLimitCores must be greater than 0.");

        if (options.JobTimeoutMinutes <= 0)
            errors.Add("Orchestrator:JobTimeoutMinutes must be greater than 0.");

        if (options.PidsLimit <= 0)
            errors.Add("Orchestrator:PidsLimit must be greater than 0.");

        if (options.MountDockerSocket && options.UseDinDSidecar)
            errors.Add("Orchestrator:MountDockerSocket and Orchestrator:UseDinDSidecar cannot both be true.");

        if (!string.IsNullOrEmpty(options.DashboardPassword) && string.IsNullOrWhiteSpace(options.DashboardUsername))
            errors.Add("Orchestrator:DashboardUsername is required when DashboardPassword is set.");

        if (options.DatabaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.PostgresConnectionString))
                errors.Add("Orchestrator:PostgresConnectionString is required when DatabaseProvider is Postgres.");
        }
        else if (options.DatabaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.DatabasePath))
                errors.Add("Orchestrator:DatabasePath is required when DatabaseProvider is Sqlite.");
        }
        else
        {
            errors.Add($"Orchestrator:DatabaseProvider '{options.DatabaseProvider}' must be 'Sqlite' or 'Postgres'.");
        }

        foreach (var node in options.DockerNodes)
        {
            if (!Uri.TryCreate(node, UriKind.Absolute, out _))
                errors.Add($"Orchestrator:DockerNodes contains invalid URI: '{node}'.");
        }

        if (options.EnableRepoCache && string.IsNullOrWhiteSpace(options.CacheMountPath))
            errors.Add("Orchestrator:CacheMountPath is required when EnableRepoCache is true.");

        for (var i = 0; i < options.Profiles.Count; i++)
        {
            var p = options.Profiles[i];
            if (string.IsNullOrWhiteSpace(p.Name))
                errors.Add($"Orchestrator:Profiles[{i}]:Name is required.");
            if (p.MatchLabels.Length == 0)
                errors.Add($"Orchestrator:Profiles[{i}]:MatchLabels must contain at least one label.");
            if (p.MemoryLimitMb is <= 0)
                errors.Add($"Orchestrator:Profiles[{i}]:MemoryLimitMb must be greater than 0.");
            if (p.CpuLimitCores is <= 0)
                errors.Add($"Orchestrator:Profiles[{i}]:CpuLimitCores must be greater than 0.");
            if (p.JobTimeoutMinutes is <= 0)
                errors.Add($"Orchestrator:Profiles[{i}]:JobTimeoutMinutes must be greater than 0.");
            if (p.PidsLimit is <= 0)
                errors.Add($"Orchestrator:Profiles[{i}]:PidsLimit must be greater than 0.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}

