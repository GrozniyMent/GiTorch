# Справочник конфигурации

GiTorch настраивается через переменные окружения (с префиксом `Orchestrator__`) или через секцию `Orchestrator` в файле `appsettings.json`. При использовании стандартного `docker-compose.yml` основные параметры удобно задавать через `.env`.

## Параметры GitHub App и отбора задач

| Переменная окружения | По умолчанию | Описание |
| :--- | :--- | :--- |
| `Orchestrator__AppId` | *(обязательно)* | Идентификатор вашего GitHub App (`App ID`). |
| `Orchestrator__WebhookSecret` | *(обязательно)* | Секрет для проверки подписи входящих вебхуков GitHub (`X-Hub-Signature-256`). |
| `Orchestrator__PrivateKeyPathOrPem` | `/app/private-key.pem` | Путь к `.pem`-файлу приватного ключа внутри контейнера или сам ключ в формате PEM. |
| `Orchestrator__RequiredLabel` | `self-hosted` | Обязательная метка в `runs-on`, по которой GiTorch отбирает задачи для обработки. Задачи без этой метки игнорируются. |
| `Orchestrator__RunnerGroupId` | `1` | ID группы раннеров в организации GitHub (`1` — группа `Default`). |

## Контейнеры, ресурсы и кэширование

| Переменная окружения | По умолчанию | Описание |
| :--- | :--- | :--- |
| `Orchestrator__RunnerImage` | `ghcr.io/owner/repo/gh-runner:latest` | Docker-образ эфемерного раннера по умолчанию. |
| `Orchestrator__MaxConcurrentRunners` | `4` | Максимальное количество одновременно выполняемых задач. |
| `Orchestrator__CpuLimitCores` | `2.0` | Лимит CPU (в ядрах) на один контейнер раннера. |
| `Orchestrator__MemoryLimitMb` | `4096` | Лимит оперативной памяти (в МБ) на один контейнер раннера. |
| `Orchestrator__PidsLimit` | `512` | Максимальное число процессов (PID) внутри контейнера раннера (защита от fork-бомб). |
| `Orchestrator__JobTimeoutMinutes` | `60` | Максимальная длительность выполнения одной задачи в минутах. |
| `Orchestrator__RunnerNetworkName` | `gitorch-runners` | Имя изолированной Docker-сети для контейнеров раннеров и DinD-сайдкар. |
| `Orchestrator__EnableRepoCache` | `false` | Создаёт персональный Docker-том кэша для каждого репозитория (`<prefix>-<owner>-<repo>`). |
| `Orchestrator__CacheMountPath` | `/runner-cache` | Путь монтирования кэша внутри контейнера и значение переменной `GITORCH_CACHE_DIR`. |
| `Orchestrator__CacheVolumePrefix` | `gitorch-cache` | Префикс имён создаваемых томов кэша. |

## Приватный Docker Registry

Если образ раннера хранится в закрытом реестре (например, приватный GHCR, Harbor или AWS ECR), укажите учётные данные для автоматического `pull`:

| Переменная окружения | По умолчанию | Описание |
| :--- | :--- | :--- |
| `Orchestrator__RegistryServer` | *(авто из `RunnerImage`)* | Адрес реестра (например, `ghcr.io` или `registry.company.com`). |
| `Orchestrator__RegistryUsername` | `""` | Имя пользователя или токен-аккаунт реестра. |
| `Orchestrator__RegistryPassword` | `""` | Пароль или PAT (для GHCR — токен с правом `read:packages`). |

## Хранилище, жизненный цикл и очистка

| Переменная окружения | По умолчанию | Описание |
| :--- | :--- | :--- |
| `Orchestrator__DatabaseProvider` | `Sqlite` | Движок хранения истории: `Sqlite` (один инстанс) или `Postgres` (кластер). |
| `Orchestrator__DatabasePath` | `data/gitorch.db` | Путь к файлу SQLite при `DatabaseProvider=Sqlite`. |
| `Orchestrator__DeliveryRetentionDays` | `7` | Сколько дней хранить историю ID доставок вебхуков (защита от дублей). |
| `Orchestrator__SessionRetentionDays` | `30` | Сколько дней хранить завершённые сессии задач в базе данных. |
| `Orchestrator__RetentionCleanupIntervalMinutes` | `60` | Периодичность запуска фоновой очистки устаревших записей. |
| `Orchestrator__ShutdownTimeoutSeconds` | `30` | Время ожидания корректного завершения активных раннеров при остановке GiTorch. |

