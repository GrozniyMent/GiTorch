namespace gitorch.Configuration;

public sealed class OrchestratorConfig
{
    public string AppId { get; set; } = "";
    public string PrivateKeyPathOrPem { get; set; } = "/app/private-key.pem";
    public string WebhookSecret { get; set; } = "";
    public string RequiredLabel { get; set; } = "self-hosted";
    public int RunnerGroupId { get; set; } = 1;
    public string DockerSocket { get; set; } = "unix:///var/run/docker.sock";
    public string RunnerImage { get; set; } = "ghcr.io/owner/repo/gh-runner:latest";
    public int MaxConcurrentRunners { get; set; } = 4;
    public bool MountDockerSocket { get; set; } = false;
    public bool UseDinDSidecar { get; set; } = false;
    public string DinDImage { get; set; } = "docker:27-dind";
    public string RunnerNetworkName { get; set; } = "gitorch-runners";
    public long PidsLimit { get; set; } = 512;
    public long MemoryLimitMb { get; set; } = 4096;
    public double CpuLimitCores { get; set; } = 2.0;
    public int JobTimeoutMinutes { get; set; } = 60;
    public string DatabasePath { get; set; } = "data/gitorch.db";
    public string DatabaseProvider { get; set; } = "Sqlite"; // "Sqlite" or "Postgres"
    public string PostgresConnectionString { get; set; } = "";
    public string RedisConnectionString { get; set; } = "";
    public int DeliveryRetentionDays { get; set; } = 7;
    public int SessionRetentionDays { get; set; } = 30;
    public int RetentionCleanupIntervalMinutes { get; set; } = 60;
    public int ShutdownTimeoutSeconds { get; set; } = 30;
    public List<string> DockerNodes { get; set; } = [];

    public string DashboardUsername { get; set; } = "admin";
    public string DashboardPassword { get; set; } = "";

    public string RegistryServer { get; set; } = "";
    public string RegistryUsername { get; set; } = "";
    public string RegistryPassword { get; set; } = "";

    public bool EnableRepoCache { get; set; } = false;
    public string CacheMountPath { get; set; } = "/runner-cache";
    public string CacheVolumePrefix { get; set; } = "gitorch-cache";

    public List<RunnerProfile> Profiles { get; set; } = [];
}
