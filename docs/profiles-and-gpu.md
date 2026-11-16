# Runner Profiles, NVIDIA GPU & Docker-in-Docker

GiTorch lets you route `runs-on` labels to different container configurations — from lightweight build runners to GPU-accelerated machines or isolated Docker build environments.

## Runner Profiles (`Profiles`)

You can define named profiles with dedicated images, CPU/memory limits, timeouts, and GPU/DinD flags. A profile is selected automatically when a GitHub Actions job includes **all** labels listed in that profile's `MatchLabels`.

### Example in `appsettings.json`

```json
{
  "Orchestrator": {
    "RequiredLabel": "gitorch",
    "Profiles": [
      {
        "Name": "gpu-ml",
        "MatchLabels": ["gitorch", "gpu"],
        "RunnerImage": "ghcr.io/my-org/gh-runner-cuda:latest",
        "CpuLimitCores": 8.0,
        "MemoryLimitMb": 32768,
        "JobTimeoutMinutes": 180,
        "PidsLimit": 1024,
        "EnableGpu": true,
        "UseDinDSidecar": false
      },
      {
        "Name": "dind-heavy",
        "MatchLabels": ["gitorch", "dind", "large"],
        "CpuLimitCores": 6.0,
        "MemoryLimitMb": 16384,
        "UseDinDSidecar": true
      }
    ]
  }
}
```

The same settings can be passed via environment variables:

```env
Orchestrator__Profiles__0__Name=gpu-ml
Orchestrator__Profiles__0__MatchLabels__0=gitorch
Orchestrator__Profiles__0__MatchLabels__1=gpu
Orchestrator__Profiles__0__MemoryLimitMb=32768
Orchestrator__Profiles__0__CpuLimitCores=8
Orchestrator__Profiles__0__EnableGpu=true
```

### Example GitHub Actions Workflow

```yaml
jobs:
  train-model:
    runs-on: [self-hosted, gitorch, gpu]
    steps:
      - uses: actions/checkout@v4
      - run: nvidia-smi
```

If no profile matches the job's labels, GiTorch falls back to the global default settings. All configured profile images are pulled automatically on startup and whenever you click **Pull image** in the dashboard.

## NVIDIA GPU Passthrough (`EnableGpu`)

To run jobs on NVIDIA GPUs:

1. Install the NVIDIA driver and `nvidia-container-toolkit` on the Docker host.
2. Verify Docker GPU support works (`docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi`).
3. Set `"EnableGpu": true` on the target profile. GiTorch requests NVIDIA GPU devices automatically for matching jobs.

## Running Docker Builds Inside Runners: DinD vs. Host Socket

If your workflows run `docker build`, `docker compose`, or `testcontainers`, choose between two modes:

### Option 1: Isolated DinD Sidecar (Recommended)

Set `Orchestrator__UseDinDSidecar=true` (globally or per profile):
- Each runner receives its own short-lived Docker-in-Docker sidecar (`docker:27-dind`) on the isolated runner network.
- The runner container gets `DOCKER_HOST=tcp://<runner>-dind:2375`.
- Builds cannot see the host Docker daemon or sibling runners, and all temporary images/volumes are wiped when the job ends.
- You can customize the sidecar image via `Orchestrator__DinDImage`.

### Option 2: Host Docker Socket Mount

If you need shared host Docker layer caching and cannot use DinD, you can set `Orchestrator__MountDockerSocket=true`.

> **Security warning:** mounting `/var/run/docker.sock` gives CI jobs effective root access to the host machine. Only enable this for trusted private repositories.
