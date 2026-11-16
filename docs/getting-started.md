# Getting Started & GitHub App Setup

This guide walks you through deploying GiTorch from scratch and connecting it to your GitHub repository or organization.

## 1. Create a GitHub App

GiTorch uses a GitHub App to receive workflow job events and issue short-lived Just-In-Time (JIT) runner registration tokens.

1. Open your GitHub account or organization settings: **Settings → Developer settings → GitHub Apps → New GitHub App**.
2. Fill in the basic details:
   - **GitHub App name**: e.g. `GiTorch Runner` (must be globally unique on GitHub).
   - **Homepage URL**: URL of your GiTorch dashboard or repository.
   - **Webhook**: check **Active**.
   - **Webhook URL**: public URL of your instance with the `/webhook` path, e.g. `https://runners.example.com/webhook`.
   - **Webhook secret**: generate a strong random string and save it for `GITHUB_WEBHOOK_SECRET`.
3. Configure permissions (**Repository permissions** / **Organization permissions**):
   - For **personal or individual repositories**:
     - **Administration**: Read and write
     - **Actions**: Read-only
     - **Metadata**: Read-only
   - For **organizations** (recommended when serving multiple repositories):
     - **Organization permissions → Self-hosted runners**: Read and write.
4. Under **Subscribe to events**, check **Workflow job** (`workflow_job`).
5. Click **Create GitHub App**.
6. On the app's settings page:
   - Copy the **App ID** (used for `GITHUB_APP_ID`).
   - Under **Private keys**, click **Generate a private key** to download the `.pem` file. Rename it to `private-key.pem` and place it next to `docker-compose.yml`.
7. Open the **Install App** tab on the left and install the app on your organization or selected repositories.

## 2. Prepare Environment Configuration

Create a `.env` file in the project directory:

```env
GITHUB_APP_ID=123456
GITHUB_WEBHOOK_SECRET=your-strong-webhook-secret

# Runner image (built locally or pulled from GHCR)
RUNNER_IMAGE=gitorch-runner:latest

# Isolated Docker builds inside runners
USE_DIND_SIDECAR=true

# Per-repository persistent cache volume
ENABLE_REPO_CACHE=true

# Web dashboard authentication
DASHBOARD_USER=admin
DASHBOARD_PASSWORD=change-this-password
```

If you want to build the bundled runner image locally instead of pulling a prebuilt image:

```bash
docker compose --profile build-only build runner-image
```

## 3. Start GiTorch

Start the service in the background:

```bash
docker compose up -d
```

Verify the health endpoint:

```bash
curl http://localhost:8080/health
```

Expected response:

```json
{"status":"healthy"}
```

Open `http://localhost:8080` in your browser and sign in with the username and password from `.env`.

## 4. Run a Test Workflow

Add `.github/workflows/ci.yml` to one of your connected repositories:

```yaml
name: Ephemeral Runner Test

on: [workflow_dispatch]

jobs:
  smoke-test:
    runs-on: [self-hosted, linux, x64]
    steps:
      - uses: actions/checkout@v4
      - name: Check environment
        run: |
          uname -a
          whoami
```

Trigger the workflow manually from the **Actions** tab. In the GiTorch dashboard, the job will immediately appear as `Queued`, move through `Provisioning` to `Running` (with live logs available via **Logs**), and its container will be automatically removed once finished.
