# Профили раннеров, NVIDIA GPU и Docker-in-Docker

GiTorch позволяет гибко маршрутизировать задачи из `runs-on` в разные конфигурации контейнеров - от лёгких сборщиков до мощных GPU-раннеров или изолированных сред для сборки Docker-образов.

## Профили раннеров (`Profiles`)

Вы можете определить именованные профили с собственным образом, лимитами CPU/RAM, таймаутами и флагами GPU/DinD. Профиль выбирается автоматически, если задача в GitHub Actions содержит **все** метки из `MatchLabels` этого профиля.

### Пример настройки через `appsettings.json`

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

Эти же настройки можно передавать переменными окружения:

```env
Orchestrator__Profiles__0__Name=gpu-ml
Orchestrator__Profiles__0__MatchLabels__0=gitorch
Orchestrator__Profiles__0__MatchLabels__1=gpu
Orchestrator__Profiles__0__MemoryLimitMb=32768
Orchestrator__Profiles__0__CpuLimitCores=8
Orchestrator__Profiles__0__EnableGpu=true
```

### Пример workflow в GitHub Actions

```yaml
jobs:
  train-model:
    runs-on: [self-hosted, gitorch, gpu]
    steps:
      - uses: actions/checkout@v4
      - run: nvidia-smi
```

Если ни один профиль не подошёл под метки задачи, применяются глобальные значения по умолчанию (`default`). Все настроенные образы автоматически подтягиваются при старте GiTorch и по кнопке **Pull image** в веб-панели.

## Запуск с NVIDIA GPU (`EnableGpu`)

Для проброса видеокарт NVIDIA в контейнеры раннеров:

1. Установите на хост драйвер NVIDIA и `nvidia-container-toolkit`.
2. Убедитесь, что Docker настроен с поддержкой GPU (`docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi`).
3. Укажите `"EnableGpu": true` в нужном профиле. GiTorch запросит все доступные GPU-устройства через драйвер `nvidia` для каждой задачи этого профиля.

## Сборка Docker-образов внутри раннеров: DinD vs Socket

Если в ваших workflow запускаются команды `docker build`, `docker compose` или `testcontainers`, есть два режима:

### Вариант 1: Изолированный DinD-сайдкар (рекомендуется)

Включите `Orchestrator__UseDinDSidecar=true` (глобально или на уровне профиля):
- Для каждого раннера поднимается персональный временный сайдкар-контейнер (`docker:27-dind`), подключённый к той же изолированной Docker-сети.
- Раннер получает переменную `DOCKER_HOST=tcp://<runner>-dind:2375`.
- Пользовательские контейнеры **не видят** демона хоста и другие раннеры, а все временные образы и тома исчезают сразу после завершения задачи.
- На уровне профиля при необходимости можно переопределить образ сайдкара (`Orchestrator__DinDImage`).

### Вариант 2: Проброс Docker-сокета хоста

Если требуется общий кэш слоёв Docker на хосте или запуск непривилегированных контейнеров не подходит, можно включить `Orchestrator__MountDockerSocket=true`.

> **Предупреждение о безопасности:** монтирование `/var/run/docker.sock` внутрь контейнера раннера фактически даёт выполняемому CI-коду полный root-доступ к хосту. Используйте этот режим **только для доверенных приватных репозиториев**.

