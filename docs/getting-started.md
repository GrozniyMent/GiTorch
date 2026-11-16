# Быстрый старт и настройка GitHub App

Это руководство поможет развернуть GiTorch с нуля и подключить его к вашему репозиторию или организации GitHub.

## 1. Создание GitHub App

GiTorch использует GitHub App для получения событий о задачах workflow и выдачи одноразовых токенов регистрации (Just-In-Time runners).

1. Откройте настройки вашего аккаунта или организации в GitHub: **Settings → Developer settings → GitHub Apps → New GitHub App**.
2. Заполните основные поля:
   - **GitHub App name**: например, `GiTorch Runner` (имя должно быть уникальным в пределах GitHub).
   - **Homepage URL**: URL вашей панели GiTorch или репозитория.
   - **Webhook**: отметьте **Active**.
   - **Webhook URL**: публичный адрес вашего сервера с путём `/webhook`, например `https://runners.example.com/webhook`.
   - **Webhook secret**: сгенерируйте надёжную случайную строку и сохраните её для `GITHUB_WEBHOOK_SECRET`.
3. Настройте права доступа (**Repository permissions** / **Organization permissions**):
   - Для работы в рамках **личных или отдельных репозиториев**:
     - **Administration**: Read and write (или **Self-hosted runners** на уровне организации).
     - **Actions**: Read-only.
     - **Metadata**: Read-only.
   - Для работы на уровне **организации** (рекомендуется для нескольких репозиториев):
     - **Organization permissions → Self-hosted runners**: Read and write.
4. В разделе **Subscribe to events** отметьте **Workflow job** (`workflow_job`).
5. Нажмите **Create GitHub App**.
6. На странице созданного приложения:
   - Скопируйте **App ID** (понадобится для `GITHUB_APP_ID`).
   - В разделе **Private keys** нажмите **Generate a private key** — скачается `.pem`-файл. Переименуйте его в `private-key.pem` и положите рядом с `docker-compose.yml`.
7. Откройте вкладку **Install App** слева и установите приложение в нужную организацию или на выбранные репозитории.

## 2. Подготовка окружения

Создайте файл `.env` в корневой директории с проектом:

```env
GITHUB_APP_ID=123456
GITHUB_WEBHOOK_SECRET=your-strong-webhook-secret

# Образ раннера (собирается локально или загружается из GHCR)
RUNNER_IMAGE=gitorch-runner:latest

# Изолированные Docker-сборки внутри раннеров
USE_DIND_SIDECAR=true

# Общий том кэша для ускорения сборок по каждому репозиторию
ENABLE_REPO_CACHE=true

# Авторизация в веб-интерфейсе
DASHBOARD_USER=admin
DASHBOARD_PASSWORD=change-this-password
```

Если вы используете собственный образ раннера из репозитория, соберите его заранее:

```bash
docker compose --profile build-only build runner-image
```

## 3. Запуск

Запустите GiTorch в фоновом режиме:

```bash
docker compose up -d
```

Проверьте состояние сервиса:

```bash
curl http://localhost:8080/health
```

Ожидаемый ответ:

```json
{"status":"healthy"}
```

Откройте веб-панель в браузере по адресу `http://localhost:8080` и войдите с логином и паролем из `.env`.

## 4. Тестовый workflow в GitHub Actions

Добавьте или обновите workflow-файл `.github/workflows/ci.yml` в одном из подключённых репозиториев:

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

Запустите workflow вручную во вкладке **Actions**. В веб-панели GiTorch задача сразу появится со статусом `Queued`, перейдёт в `Provisioning`, затем в `Running` (доступны живые логи по кнопке **Logs**) и после завершения контейнер будет автоматически удалён.

