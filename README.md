# TaskApi

Тестовое задание .NET 9 Minimal API для управления задачами (TaskItem) с PostgreSQL (EF Core)
и публикацией события в RabbitMQ при завершении задачи.

## Запуск

Нужны PostgreSQL и RabbitMQ. Проще всего через Docker

```bash
docker run -d --name pg -e POSTGRES_PASSWORD=postgres -p 54325432 postgres17
docker run -d --name rabbit -p 56725672 -p 1567215672 rabbitmq4-management
```

Строка подключения к БД — в `appsettings.json` (`ConnectionStringsPostgres`), параметры брокера —
в секции `RabbitMq`. Миграции применяются автоматически при старте (см. HACK в `Program.cs`).

```bash
dotnet run --project srcTaskApi
dotnet test
```

 Тесты поднимают реальные PostgreSQL и RabbitMQ через Testcontainers — нужен запущенный Docker.

## API

 Метод  Эндпоинт  Что делает 
-----------------------------
 POST  `tasks`  создаёт задачу (по умолчанию `IsCompleted=false`, `Priority=Medium`) 
 GET  `tasks`  возвращает все задачи (без пагинации) 
 PUT  `tasks{id}complete`  завершает задачу, ставит `CompletedAt`, шлёт событие в RabbitMQ 
 DELETE  `tasks{id}`  жёстко удаляет задачу 

Событие при завершении публикуется в обменник `task.events` (topic, durable) с routing key
`task.completed`, тело `{ taskId, title, completedAt, priority }` (priority строкой).

## Структура

```
srcTaskApi
├── Program.cs              — DI, конфигурация, обработка ошибок, app.MapTaskEndpoints()
├── TaskEndpoints.cs        — все 4 эндпоинта (валидация, 409, публикация)
├── TaskItem.cs             — сущность + enum Priority
├── Contracts.cs            — CreateTaskRequest, TaskResponse, TaskCompletedEvent
├── Data                   — TaskDbContext + IEntityTypeConfiguration
├── Messaging              — ITaskEventPublisher, RabbitMqPublisher (ChannelT + BackgroundService)
└── Migrations
testsTaskApi.Tests
├── ApiFixture.cs                  — Testcontainers PostgreSQL + WebApplicationFactory + фейк публикатора
└── CompleteTaskIntegrationTest.cs — сценарий create → complete → 409
```

## Ключевые решения

- Оптимистичная конкурентность через `xmin`. `uint Version` + `.IsRowVersion()`; Npgsql мапит это
  на системную колонку PostgreSQL `xmin` (отдельной колонки в миграции не создаётся). В `complete`
  две проверки `IsCompleted == true` → 409 (второй запрос пришёл после коммита первого) и
  `DbUpdateConcurrencyException` → 409 (настоящая гонка). Без пессимистичных локов.
- Публикация без блокировки запроса. Эндпоинт делает `Enqueue()` в in-memory `ChannelT` и сразу
  возвращает ответ. Один `BackgroundService` читает очередь и публикует — один читатель, потому что
  `IChannel` в RabbitMQ.Client не потокобезопасен для параллельных публикаций.
- Порядок сначала БД, потом событие. Публикуем только после успешного `SaveChangesAsync`, чтобы
  подписчики не узнали о завершении, которого в БД нет.
- Сбой брокера не валит запрос. Ошибки RabbitMQ ловятся в фоне, логируются с `TaskId`, сообщение
  отбрасывается (fail silently, но с логом).
- Graceful shutdown. `Writer.Complete()` → дренаж очереди (максимум 5 с) → закрытие канала и
  соединения. `stoppingToken` намеренно НЕ передаётся в `ReadAllAsync`, иначе дренаж оборвался бы сразу.
  `HostOptions.ShutdownTimeout = 10 с`, чтобы хост не прибил сервис раньше наших 5 с.
- Простая структура. Один web-проект, файлы по ответственности, без слоёв ApplicationDomain
  Infrastructure и без репозиториев поверх EF Core 4 эндпоинта и одна сущность этого не требуют.

Подробные ADR — в [`docsdecisions.md`](docsdecisions.md).

## Сознательно срезанные углы (HACK)

Помечены в коде комментарием ` HACK не успел, доделал бы так ...`.

 Где  Что срезано  Как доделал бы 
----------------------------------
 `Program.cs`  Миграции применяются в стартапе  Отдельный шаг деплоя (`dotnet ef migrations bundle`  `database update` в CICD), чтобы инстансы не гонялись за `MigrateAsync` и схему можно было откатывать независимо 
 `TaskEndpoints.cs` (GET)  Нет пагинации  Query-параметры `page&pageSize` или keyset по `CreatedAt+Id`, ответ `PagedResponse` с `total``hasMore` 
 `TaskEndpoints.cs` (complete)  Между `SaveChanges` и `Enqueue` есть окно потери события  Транзакционный Outbox запись события в таблицу `outbox` в той же транзакции + фоновый relay (at-least-once) 
 `RabbitMqPublisher.cs`  Нет реконнекта при недоступности брокера на старте  Переподключение с backoff через Polly (`ResiliencePipeline`) 
 `RabbitMqPublisher.cs`  Нет publisher confirms  `CreateChannelOptions { PublisherConfirmationsEnabled = true }` + ожидание acknack 
 `ApiFixture.cs` (тест)  Публикатор заглушён фейком  Testcontainers RabbitMQ + временная очередь, забинженная на `task.events``task.completed`, проверка реального сообщения через `BasicConsume` 

## О разработке

Код написан с помощью Claude Code с набором [dotnet-claude-kit](httpsgithub.com), использованным
выборочно взяты workflow и практики, совпадающие со спецификацией (`TimeProvider`, `TypedResults`,
ProblemDetails, Testcontainers, `IEntityTypeConfiguration`, `sealed`, `CancellationToken`), а дефолты,
противоречащие ТЗ (.NET 10C# 14, WolverineMassTransit, FluentValidation, Result-паттерн,
обязательная пагинация), осознанно отключены. Подробности — в `CLAUDE.md` и `docsdecisions.md`.
