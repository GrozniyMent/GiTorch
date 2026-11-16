<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/logo-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/logo-light.svg" />
  <img src="docs/assets/logo-dark.svg" alt="GiTorch Logo" width="96" height="96" />
</picture>

# GiTorch

**Lightweight orchestrator for ephemeral self-hosted GitHub Actions runners on Docker.**

[![CI](https://github.com/GrozniyMent/gitorch/actions/workflows/ci.yml/badge.svg)](https://github.com/GrozniyMent/gitorch/actions/workflows/ci.yml)
[![Release](https://github.com/GrozniyMent/gitorch/actions/workflows/release.yml/badge.svg)](https://github.com/GrozniyMent/gitorch/actions/workflows/release.yml)
[![GHCR](https://img.shields.io/badge/ghcr.io-grozniyment%2Fgitorch-24292e?logo=docker&logoColor=white)](https://github.com/GrozniyMent/gitorch/pkgs/container/gitorch)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

[Quick Start](#quick-start) · [Documentation](docs/getting-started.md) · [Configuration](docs/configuration.md) · [Scaling & HA](docs/scaling-and-ha.md)

</div>

---

**GiTorch** automatically provisions a clean, isolated Docker container for every GitHub Actions job and destroys it as soon as the job finishes. No leftover host state, no manual runner restarts, and no cross-build contamination.

> **All the power of ephemeral runners, profiles, and DinD isolation — without the Kubernetes complexity or overhead.**

## Why GiTorch instead of GitHub ARC?

| | GiTorch | GitHub Actions Runner Controller (ARC) |
| :--- | :--- | :--- |
| **Infrastructure** | Single Docker host (`docker compose up -d`) | Full Kubernetes cluster + Helm + cert-manager |
| **Setup time** | ~2 minutes | Hours of cluster & CRD configuration |
| **DinD isolation** | Built-in per-job DinD sidecar toggle | Requires Kubernetes pod/dind template tuning |
| **Web UI & live logs** | Built-in real-time dashboard + log streaming | None out of the box (requires kubectl / external tools) |
| **Multi-node & HA** | Optional Docker node pool + PostgreSQL/Redis HA | Tied to Kubernetes scheduling |
| **Resource footprint** | Lightweight single container (~50–80 MB RAM) | Control-plane pods + k8s system overhead |

## Key Features

- **Event-driven ephemeral runners** — spins up a single-use container on incoming GitHub Actions job events and removes it automatically upon completion or cancellation.
- **Runner profiles & NVIDIA GPU** — maps `runs-on` labels to custom images, CPU/memory limits, timeouts, and GPU passthrough.
- **Docker-in-Docker sidecars** — provides an isolated, short-lived Docker daemon per build without exposing the host Docker socket.
- **Per-repository dependency caching** — optional persistent cache volumes scoped by repository to speed up repeated builds.
- **Multi-node Docker pool & HA** — distributes runner containers across multiple Docker hosts and supports multi-replica high availability backed by PostgreSQL and Redis.
- **Real-time web dashboard** — live container logs, cluster metrics, job search and filtering, force termination, and automatic light/dark browser theme support.
- **Observability & security** — built-in Prometheus metrics (`/metrics`), health checks (`/health`), webhook signature verification, duplicate delivery protection, and form-based dashboard authentication via `/login`.

---

## Quick Start

### 1. Prepare your GitHub App key and `.env`

Place your GitHub App private key next to `docker-compose.yml` as `private-key.pem` and create a `.env` file:

```env
GITHUB_APP_ID=123456
GITHUB_WEBHOOK_SECRET=your-webhook-secret

RUNNER_IMAGE=ghcr.io/grozniyment/gitorch-runner:latest
USE_DIND_SIDECAR=true
ENABLE_REPO_CACHE=true

DASHBOARD_USER=admin
DASHBOARD_PASSWORD=change-this-password
```

### 2. Start with Docker Compose

```bash
docker compose up -d
```

Verify the service is healthy:

```bash
curl http://localhost:8080/health
```

Open the dashboard at `http://localhost:8080`, sign in with your configured credentials, and trigger a workflow targeting:

```yaml
runs-on: [self-hosted, linux, x64]
```

### 3. No public IP or domain? Expose locally in 10 seconds (Optional)

You don't need a VPS, domain, or SSL certificate to test GiTorch locally. Expose port `8080` over HTTPS with a one-liner and paste the generated `<tunnel-url>/webhook` into your GitHub App settings:

- **Cloudflare Tunnel (no account required):**
  ```bash
  cloudflared tunnel --url http://localhost:8080
  ```
- **ngrok:**
  ```bash
  ngrok http 8080
  ```

> For step-by-step GitHub App setup and first-run instructions, see the [Getting Started Guide](docs/getting-started.md).

---

## Core Environment Variables

| Variable | Default | Description |
| :--- | :--- | :--- |
| `GITHUB_APP_ID` | *(required)* | Your GitHub App ID |
| `GITHUB_WEBHOOK_SECRET` | *(required)* | Secret used to verify incoming webhook signatures |
| `RUNNER_IMAGE` | `gitorch-runner:latest` | Default runner Docker image |
| `USE_DIND_SIDECAR` | `true` | Enable isolated DinD sidecar containers for Docker builds |
| `ENABLE_REPO_CACHE` | `true` | Mount a persistent cache volume per repository |
| `DASHBOARD_USER` | `admin` | Username for dashboard authentication |
| `DASHBOARD_PASSWORD` | `""` | Dashboard password (leave empty to disable authentication) |

For all resource limits, profiles, private registry settings, PostgreSQL/Redis options, and retention policies, see the [Configuration Reference](docs/configuration.md).

---

## Documentation

- [Getting Started & GitHub App Setup](docs/getting-started.md) — creating your GitHub App, preparing the environment, and running your first workflow.
- [Configuration Reference](docs/configuration.md) — all environment variables, container limits, private registries, and retention settings.
- [Runner Profiles, NVIDIA GPU & DinD](docs/profiles-and-gpu.md) — routing `runs-on` labels to custom profiles, enabling GPU passthrough, and isolating Docker builds.
- [Scaling & High Availability](docs/scaling-and-ha.md) — distributing runners across multiple Docker hosts and running a multi-replica HA cluster.
- [Monitoring, Dashboard & Security](docs/monitoring-and-security.md) — managing jobs, authentication, Prometheus metrics, and production hardening.

---

## License

Licensed under the [Apache License 2.0](LICENSE).
