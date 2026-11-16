# Масштабирование и отказоустойчивость (HA)

GiTorch поддерживает как горизонтальное распределение контейнеров раннеров по нескольким Docker-хостам, так и запуск нескольких реплик самого оркестратора для высокой доступности.

## 1. Пул из нескольких Docker-хостов (Multi-Node)

Если мощностей одного сервера не хватает для параллельных сборок, подключите дополнительные Docker-демоны по TCP или SSH/TLS. GiTorch будет равномерно распределять новые контейнеры раннеров между всеми узлами (Round-Robin) и показывать загрузку каждого узла в веб-панели.

### Конфигурация

Укажите список адресов узлов через `DockerNodes`:

```env
Orchestrator__DockerNodes__0=unix:///var/run/docker.sock
Orchestrator__DockerNodes__1=tcp://docker-worker-01.internal:2375
Orchestrator__DockerNodes__2=tcp://docker-worker-02.internal:2375
Orchestrator__MaxConcurrentRunners=12
```

Либо в `appsettings.json`:

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

> **Рекомендация:** изолируйте порт удалённого Docker API в частной сети (WireGuard/VPN/внутренний VPC) или используйте TLS-аутентификацию, не открывайте `2375` в публичный интернет.

## 2. Несколько реплик оркестратора (High Availability)

Для защиты от перезапуска или отказа одного инстанса GiTorch можно запустить две и более реплик за обратным прокси (Nginx, Traefik, HAProxy, Caddy).

### Требования для HA-кластера

1. **PostgreSQL** (`DatabaseProvider=Postgres`) - хранит общую распределённую очередь задач, защиту от повторной доставки вебхуков и историю сессий, доступную всем репликам.
2. **Redis** (`RedisConnectionString`) - синхронизирует живые обновления веб-интерфейса между репликами (backplane).

### Пример переменных окружения для HA

```env
Orchestrator__DatabaseProvider=Postgres
Orchestrator__PostgresConnectionString=Host=postgres;Port=5432;Database=gitorch;Username=gitorch;Password=secret
Orchestrator__RedisConnectionString=redis:6379
```

### Пример `docker-compose.ha.yml`

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
    image: ghcr.io/<owner>/gitorch:latest
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

На балансировщике нагрузки включите поддержку WebSocket и sticky sessions (привязка по cookie или IP) для корректной работы живого обновления панели.

