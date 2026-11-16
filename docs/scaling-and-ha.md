# Scaling & High Availability (HA)

GiTorch supports both distributing runner containers across multiple Docker hosts and running multiple orchestrator replicas for high availability.

## 1. Multi-Node Docker Pool

When a single server cannot handle all concurrent builds, add remote Docker daemons over TCP or TLS. GiTorch distributes new runner containers evenly across all configured nodes (round-robin) and displays per-node load in the dashboard.

### Configuration

Specify the list of node endpoints using `DockerNodes`:

```env
Orchestrator__DockerNodes__0=unix:///var/run/docker.sock
Orchestrator__DockerNodes__1=tcp://docker-worker-01.internal:2375
Orchestrator__DockerNodes__2=tcp://docker-worker-02.internal:2375
Orchestrator__MaxConcurrentRunners=12
```

Or in `appsettings.json`:

```json
{
  "Orchestrator": {
    "DockerNodes": [
      "unix:///var/run/docker.sock",
      "tcp://docker-worker-01.internal:2375",
      "tcp://docker-worker-02.internal:2375"
    ],
    "MaxConcurrentRunners": 12
  }
}
```

> **Recommendation:** keep remote Docker API ports inside a private network (WireGuard/VPN/VPC) or behind mutual TLS; never expose port `2375` to the public internet.

## 2. Multi-Replica High Availability

To eliminate downtime during restarts or upgrades, run two or more GiTorch replicas behind a reverse proxy (Nginx, Traefik, HAProxy, or Caddy).

### HA Requirements

1. **PostgreSQL** (`DatabaseProvider=Postgres`) — stores the shared distributed job queue, webhook deduplication table, and session history across all replicas.
2. **Redis** (`RedisConnectionString`) — synchronizes live dashboard updates across replicas.

### Example HA Environment Variables

```env
Orchestrator__DatabaseProvider=Postgres
Orchestrator__PostgresConnectionString=Host=postgres;Port=5432;Database=gitorch;Username=gitorch;Password=secret
Orchestrator__RedisConnectionString=redis:6379
```

### Example `docker-compose.ha.yml`

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: gitorch
      POSTGRES_USER: gitorch
      POSTGRES_PASSWORD: secret
    volumes:
      - pg_data:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine

  gitorch:
    image: ghcr.io/grozniyment/gitorch:latest
    deploy:
      replicas: 2
    env_file: .env
    environment:
      Orchestrator__DatabaseProvider: Postgres
      Orchestrator__PostgresConnectionString: Host=postgres;Port=5432;Database=gitorch;Username=gitorch;Password=secret
      Orchestrator__RedisConnectionString: redis:6379
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - ./private-key.pem:/app/private-key.pem:ro
    depends_on:
      - postgres
      - redis

volumes:
  pg_data:
```

Make sure your reverse proxy enables WebSocket upgrades and sticky sessions (cookie or client IP affinity) for smooth real-time dashboard updates.
