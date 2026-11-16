# Configuration Reference

GiTorch is configured via environment variables (prefixed with `Orchestrator__`) or the `Orchestrator` section in `appsettings.json`. When using the provided `docker-compose.yml`, common settings can be placed directly in `.env`.

## GitHub App & Job Matching

| Environment Variable | Default | Description |
| :--- | :--- | :--- |
| `Orchestrator__AppId` | *(required)* | GitHub App ID. |
| `Orchestrator__WebhookSecret` | *(required)* | Secret used to validate incoming GitHub webhook signatures (`X-Hub-Signature-256`). |
| `Orchestrator__PrivateKeyPathOrPem` | `/app/private-key.pem` | Path to the `.pem` private key file inside the container or inline PEM content. |
| `Orchestrator__RequiredLabel` | `self-hosted` | Label that must be present in `runs-on` for GiTorch to pick up a job. |
| `Orchestrator__RunnerGroupId` | `1` | Target GitHub organization runner group ID (`1` = `Default`). |

## Containers, Resource Limits & Caching

| Environment Variable | Default | Description |
| :--- | :--- | :--- |
| `Orchestrator__RunnerImage` | `ghcr.io/owner/repo/gh-runner:latest` | Default Docker image for ephemeral runners. |
| `Orchestrator__MaxConcurrentRunners` | `4` | Maximum number of concurrent runner containers. |
| `Orchestrator__CpuLimitCores` | `2.0` | CPU limit (cores) per runner container. |
| `Orchestrator__MemoryLimitMb` | `4096` | Memory limit (MB) per runner container. |
| `Orchestrator__PidsLimit` | `512` | Maximum number of processes (PIDs) per runner container. |
| `Orchestrator__JobTimeoutMinutes` | `60` | Maximum execution time per job in minutes. |
| `Orchestrator__RunnerNetworkName` | `gitorch-runners` | Isolated Docker bridge network name for runners and DinD sidecars. |
| `Orchestrator__EnableRepoCache` | `false` | Enables a dedicated persistent Docker cache volume per repository (`<prefix>-<owner>-<repo>`). |
| `Orchestrator__CacheMountPath` | `/runner-cache` | Mount path inside the runner container and value of `GITORCH_CACHE_DIR`. |
| `Orchestrator__CacheVolumePrefix` | `gitorch-cache` | Prefix for generated cache volume names. |

## Private Docker Registry

If your runner images are hosted in a private registry (such as private GHCR, Harbor, or AWS ECR), provide credentials for automatic pulls:

| Environment Variable | Default | Description |
| :--- | :--- | :--- |
| `Orchestrator__RegistryServer` | *(inferred from `RunnerImage`)* | Registry hostname (e.g. `ghcr.io` or `registry.company.com`). |
| `Orchestrator__RegistryUsername` | `""` | Registry username. |
| `Orchestrator__RegistryPassword` | `""` | Registry password or access token (e.g. a PAT with `read:packages` for GHCR). |

## Storage, Lifecycle & Retention

| Environment Variable | Default | Description |
| :--- | :--- | :--- |
| `Orchestrator__DatabaseProvider` | `Sqlite` | Storage backend: `Sqlite` (single instance) or `Postgres` (multi-replica cluster). |
| `Orchestrator__DatabasePath` | `data/gitorch.db` | SQLite database file path when `DatabaseProvider=Sqlite`. |
| `Orchestrator__DeliveryRetentionDays` | `7` | Number of days to retain webhook delivery IDs for deduplication. |
| `Orchestrator__SessionRetentionDays` | `30` | Number of days to retain completed job sessions in the database. |
| `Orchestrator__RetentionCleanupIntervalMinutes` | `60` | Interval in minutes between background retention cleanup runs. |
| `Orchestrator__ShutdownTimeoutSeconds` | `30` | Graceful shutdown window to wait for active runners before stopping them. |
