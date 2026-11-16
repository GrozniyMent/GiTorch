<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/logo-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/logo-light.svg" />
  <img src="docs/assets/logo-dark.svg" alt="GiTorch Logo" width="96" height="96" />
</picture>

# GiTorch

**Лёгкий оркестратор эфемерных self-hosted раннеров GitHub Actions на базе Docker.**

[![CI](https://github.com/GrozniyMent/gitorch/actions/workflows/ci.yml/badge.svg)](https://github.com/GrozniyMent/gitorch/actions/workflows/ci.yml)
[![Release](https://github.com/GrozniyMent/gitorch/actions/workflows/release.yml/badge.svg)](https://github.com/GrozniyMent/gitorch/actions/workflows/release.yml)
[![GHCR](https://img.shields.io/badge/ghcr.io-grozniyment%2Fgitorch-24292e?logo=docker&logoColor=white)](https://github.com/GrozniyMent/gitorch/pkgs/container/gitorch)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

[Быстрый старт](#быстрый-старт) · [Документация](docs/getting-started.md) · [Конфигурация](docs/configuration.md) · [Масштабирование](docs/scaling-and-ha.md)

</div>

---

**GiTorch** автоматически поднимает изолированный чистый Docker-контейнер под каждую задачу GitHub Actions и удаляет его сразу после завершения. Никакого накопленного мусора на хосте, ручного перезапуска раннеров и утечки состояния между сборками.

## Ключевые возможности

- **Эфемерные раннеры по событию** - мгновенный запуск одноразового контейнера при поступлении задачи из GitHub Actions с автоматическим удалением по завершении или отмене workflow.
- **Профили и NVIDIA GPU** - сопоставление тегов `runs-on` с нужными образами, лимитами CPU/RAM, таймаутами и пробросом GPU.
- **Docker-in-Docker сайдкары** - изолированный временный Docker-демон для каждой сборки без проброса сокета хоста.
- **Кэширование зависимостей** - опциональные постоянные тома кэша с привязкой к конкретному репозиторию для ускорения повторных сборок.
- **Пул Docker-узлов и HA** - равномерное распределение раннеров по нескольким Docker-хостам и поддержка отказоустойчивой работы нескольких реплик с PostgreSQL и Redis.
- **Веб-панель реального времени** - живые логи контейнеров, метрики кластера, поиск и фильтрация задач, принудительная остановка, светлая и тёмная темы с автоопределением темы браузера.
- **Наблюдаемость и безопасность** - встроенные метрики Prometheus (`/metrics`), healthcheck (`/health`), проверка подписи вебхуков, защита от дублирующейся доставки.

---

## Быстрый старт

### 1. Подготовьте ключ GitHub App и файл `.env`

Положите приватный ключ вашего GitHub App рядом с `docker-compose.yml` под именем `private-key.pem` и создайте `.env`:

```env
GITHUB_APP_ID=123456
GITHUB_WEBHOOK_SECRET=your-webhook-secret

RUNNER_IMAGE=ghcr.io/grozniyment/gitorch-runner:latest
USE_DIND_SIDECAR=true
ENABLE_REPO_CACHE=true

DASHBOARD_USER=admin
DASHBOARD_PASSWORD=change-this-password
```

### 2. Запустите через Docker Compose

```bash
docker compose up -d
```

Проверьте, что сервис запущен:

```bash
curl http://localhost:8080/health
```

Откройте панель управления по адресу `http://localhost:8080`, войдите с заданными учётными данными и отправьте тестовый workflow с метками:

```yaml
runs-on: [self-hosted, linux, x64]
```

> Подробная инструкция по регистрации GitHub App и первому запуску - в руководстве [Быстрый старт](docs/getting-started.md).

---

## Основные параметры окружения

| Переменная | По умолчанию | Назначение |
| :--- | :--- | :--- |
| `GITHUB_APP_ID` | *(обязательно)* | App ID вашего GitHub App |
| `GITHUB_WEBHOOK_SECRET` | *(обязательно)* | Секрет подписи входящих вебхуков |
| `RUNNER_IMAGE` | `gitorch-runner:latest` | Базовый Docker-образ раннера |
| `USE_DIND_SIDECAR` | `true` | Запуск изолированного DinD-сайдкара для Docker-сборок |
| `ENABLE_REPO_CACHE` | `true` | Персональный Docker-том кэша для каждого репозитория |
| `DASHBOARD_USER` | `admin` | Имя пользователя для входа в веб-панель |
| `DASHBOARD_PASSWORD` | `""` | Пароль веб-панели (если пусто - вход свободный) |

Все параметры лимитов, профилей, приватных реестров, PostgreSQL/Redis и мониторинга описаны в [Справочнике конфигурации](docs/configuration.md).

---

## Документация

- [Быстрый старт и настройка GitHub App](docs/getting-started.md) - регистрация приложения в GitHub, подготовка окружения и запуск первого workflow.
- [Справочник конфигурации](docs/configuration.md) - все переменные окружения, лимиты контейнеров, приватные реестры и политика хранения истории.
- [Профили раннеров, NVIDIA GPU и DinD](docs/profiles-and-gpu.md) - настройка кастомных профилей под теги `runs-on`, подключение GPU и безопасные Docker-сборки.
- [Масштабирование и отказоустойчивость](docs/scaling-and-ha.md) - распределение нагрузки между несколькими Docker-хостами и кластер из нескольких реплик.
- [Мониторинг, веб-панель и безопасность](docs/monitoring-and-security.md) - управление задачами, авторизация, Prometheus-метрики и рекомендации для продакшена.

---

## Лицензия

Проект распространяется под лицензией [Apache License 2.0](LICENSE).

