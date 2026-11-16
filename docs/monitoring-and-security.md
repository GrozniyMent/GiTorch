# Monitoring, Dashboard & Security

## Web Dashboard

The web dashboard is served at `/` and updates in real time:
- **Browser theme sync**: automatically follows your OS/browser light or dark theme, with a manual `system` / `light` / `dark` switcher in the header.
- **Cluster overview**: active/available slots, queued jobs, average slot wait and job duration, GitHub API errors, active Docker nodes, and configured profiles.
- **Job management**:
  - Filter by status (`All`, `Active`, `Completed`, `Failed`, `Cancelled`) and search by `#id`, repository, runner name, container ID, or label (filter state is synced with the URL).
  - Stream live container output via **Logs** with `STDERR` filtering, timestamp toggling, in-log search, clipboard copy, and `.log` download.
  - Cancel queued jobs or force-kill stuck containers (`Kill` / `Cancel`) with inline confirmation.
  - Trigger manual runner image updates via **Pull image**.

## Authentication

When `Orchestrator__DashboardPassword` is set, the dashboard is protected:
- Browser requests are redirected to `/login` and receive a signed `HttpOnly` session cookie.
- Automation scripts and `curl` can also authenticate using `Authorization: Basic ...`.
- Leaving `DashboardPassword` empty disables authentication (recommended only for local testing or behind an authenticating reverse proxy / VPN).

## Prometheus Metrics (`/metrics`) & Health Check (`/health`)

Both `/health` and `/metrics` bypass dashboard authentication so load balancers and Prometheus can scrape them directly.

- **`GET /health`** — returns `200 OK` (`{"status":"healthy"}`) when the orchestrator is ready.
- **`GET /metrics`** — exposes Prometheus-formatted metrics:
  - `gitorch_queued_jobs` — number of queued or provisioning jobs.
  - `gitorch_active_runners` — number of currently active runner slots.
  - `gitorch_available_slots` — number of available runner slots.
  - `gitorch_github_api_errors_total` — total GitHub API errors.
  - `gitorch_slot_wait_seconds` — time spent waiting for a runner slot (`_sum`, `_count`).
  - `gitorch_job_duration_seconds` — duration of executed runner jobs (`_sum`, `_count`).
  - `gitorch_job_exits_total{status,exit_code}` — completed jobs partitioned by status and exit code.

### Example `prometheus.yml`

```yaml
scrape_configs:
  - job_name: gitorch
    scrape_interval: 15s
    static_configs:
      - targets: ["gitorch:8080"]
```

## Production Security Best Practices

1. **Terminate TLS in front of GiTorch**: use Nginx, Traefik, or Caddy with HTTPS so login credentials and GitHub webhook payloads are encrypted in transit.
2. **Always set `GITHUB_WEBHOOK_SECRET` and `DASHBOARD_PASSWORD`**: use long, randomly generated secrets.
3. **Protect the GitHub App private key**: mount `private-key.pem` read-only (`:ro`) and restrict host permissions (`chmod 600 private-key.pem`).
4. **Never mount `/var/run/docker.sock` for untrusted/public repositories**: keep `MountDockerSocket=false` and use `UseDinDSidecar=true` when Docker builds are needed.
5. **Enforce container resource limits**: configure `MemoryLimitMb`, `CpuLimitCores`, `PidsLimit`, and `JobTimeoutMinutes` appropriately for your host capacity.
6. **Restrict `/metrics` access**: if the service is reachable from the public internet, block `/metrics` at your reverse proxy or firewall so only your monitoring network can scrape it.
