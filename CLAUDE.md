# TaskApi — test assignment (.NET Backend, Middle)

## Context
Time-boxed test task (~1.5h). The code will be discussed with the reviewer on a video call,
so the owner must be able to explain every line. Prefer simple, explicit code over clever abstractions.

- Full spec: @docs/spec.md — read before starting any stage.
- Accepted decisions: @docs/decisions.md — read before touching DbContext, the /complete endpoint or messaging.

Priority of instructions: current user instruction → this file → docs/decisions.md → docs/spec.md
→ dotnet-claude-kit skills/rules/templates → existing code.
**If a kit skill says one thing and this file says another, this file wins. Do not ask, do not "improve".**

## Stack (fixed by the spec, no substitutions)
- **.NET 9** (`net9.0`, C# 13 — no C# 14 features like `field` or extension members), ASP.NET Core Minimal API
- Solution format: `.sln` (not `.slnx` — reviewer's tooling is unknown)
- EF Core 9 + `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x, PostgreSQL 17, migrations via `dotnet ef`
- **`RabbitMQ.Client` 7.x directly** (async API: `IConnection`, `IChannel`, `BasicPublishAsync`)
- Tests: xUnit v3 + `Microsoft.AspNetCore.Mvc.Testing` 9.x + Testcontainers (PostgreSql, RabbitMq), plain `Assert`

## dotnet-claude-kit: what we use and what we override

**Use from the kit (fits the spec):**
- `TimeProvider` instead of `DateTime.*` (+ `FakeTimeProvider` in tests if needed)
- `TypedResults` with `Results<...>` union return types; errors as RFC 9457 ProblemDetails
  (`AddProblemDetails()` + `UseExceptionHandler()`)
- EF Core without repository wrappers; `IEntityTypeConfiguration<T>`; `AsNoTracking` for reads; `ExecuteDeleteAsync`
- `sealed` classes, `sealed record` DTOs, primary constructors, `CancellationToken` passed through every async call
- Integration tests on `WebApplicationFactory` + Testcontainers, never in-memory DB

**Override — do NOT apply even if a kit skill/rule/template requires it:**
| Kit default | Here | Why |
|---|---|---|
| .NET 10 / C# 14, `.slnx`, `Directory.Build.props` with net10.0 | net9.0, `.sln` | Spec fixes .NET 9 |
| Wolverine / MassTransit, Wolverine outbox (messaging skill) | raw `RabbitMQ.Client` + `Channel<T>` (ADR 002) | Reviewer evaluates manual publish + graceful shutdown; a framework hides exactly that |
| FluentValidation + `ValidationFilter<T>` | manual `if` checks → `TypedResults.ValidationProblem` | Spec allows `if`; less surface to defend |
| Result pattern + handler classes per operation | logic directly in endpoint methods | 4 endpoints, 1 entity — handlers add indirection only |
| `IEndpointGroup` + reflection auto-discovery | one `TaskEndpoints.MapTaskEndpoints()` extension, called from Program.cs | One group; explicit wiring is easier to find on review |
| `/scaffold` 9-point checklist (OpenAPI metadata, validator, handler, Result) | not used | Conflicts with all of the above |
| Mandatory pagination on list endpoints | `GET /tasks` returns all; pagination only as an optional bonus stage | Spec: "returns **all**" |
| `/de-sloppify` step 5 "Resolve TODOs" | **never remove `// HACK:` comments** | Spec explicitly asks for them |
| Copying kit rules into `.claude/rules/` | do not copy | They enforce net10, FluentValidation, IEndpointGroup |

## Structure
```
TaskApi.sln
src/TaskApi/
├── Program.cs              — DI, config, app.MapTaskEndpoints(), `public partial class Program;`
├── TaskEndpoints.cs        — all 4 endpoints (static extension method + private static handlers)
├── TaskItem.cs             — entity + enum Priority
├── Contracts.cs            — CreateTaskRequest, TaskResponse, TaskCompletedEvent (sealed records)
├── Data/
│   ├── TaskDbContext.cs
│   └── TaskItemConfiguration.cs   — IEntityTypeConfiguration<TaskItem>
├── Messaging/
│   ├── ITaskEventPublisher.cs     — void Enqueue(TaskCompletedEvent)
│   ├── RabbitMqOptions.cs
│   └── RabbitMqPublisher.cs       — Channel<T> + BackgroundService
├── Migrations/
└── appsettings.json
tests/TaskApi.Tests/
├── ApiFixture.cs                  — containers + WebApplicationFactory
└── CompleteTaskIntegrationTest.cs
```

## Invariants (reviewer explicitly checks these — never break them)
1. **Title**: `string.IsNullOrWhiteSpace` → 400; length after `Trim()` > 200 → 400. Return `TypedResults.ValidationProblem`.
2. **CompletedAt** is `null` on create and set only in `/complete`. Create accepts a DTO, never `TaskItem`.
3. **Publish only after** a successful `SaveChangesAsync`.
4. **/complete concurrency**: already completed → 409; `DbUpdateConcurrencyException` → 409.
   Token = PostgreSQL `xmin` via `uint Version` + `.IsRowVersion()`. No pessimistic locks.
5. **Broker failure never fails the HTTP request**: log error with TaskId, drop the message.
6. **Endpoint never awaits the broker**: enqueue into `Channel<T>`; a single `BackgroundService`
   publishes (one reader because `IChannel` is not thread-safe for concurrent publishes).
7. **Graceful shutdown**: `Writer.Complete()` → drain queue (max 5s) → `CloseAsync` channel → connection.
   Do NOT pass `stoppingToken` into `ReadAllAsync` — it would abort the drain.
8. **Time**: `DateTimeOffset` in UTC via injected `TimeProvider` (Npgsql rejects non-zero offsets for timestamptz).
9. **JSON**: camelCase, enums as strings (`JsonStringEnumConverter`), plus `Enum.IsDefined` check for numeric enum values.

## Message contract
- Exchange `task.events` (topic, durable), routing key `task.completed`
- Body: `{ "taskId", "title", "completedAt", "priority" }`, priority as string
- Props: `ContentType = application/json`, `DeliveryMode = Persistent`

## Deliberately cut corners
Mark in code with a Russian comment: `// HACK: не успел, доделал бы так: ...`
- Transactional Outbox (message lost if process dies between SaveChanges and publish)
- Publisher confirms
- Reconnect with backoff (Polly)
- `MigrateAsync()` on startup (in prod — separate deploy step / migration bundle)
- Pagination (only if time remains)

## Workflow
Stages: 1 init → 2 model+EF+migration → 3 endpoints+validation+409 (TDD) → 4 messaging (TDD)
→ 5 graceful shutdown → 6 verify + review + README → (7 bonus: pagination).

For every stage:
1. `/plan` first: inspect, propose ≤10 steps, wait for approval. Skip the architecture questionnaire —
   architecture is fixed: single project, minimal API, no layers.
2. Implement ONLY the approved stage. No unrelated refactoring or reformatting.
3. Build + tests green before reporting (`/build-fix` if red, max 5 iterations).
4. In the reply, explain each non-obvious decision in 1–2 lines (owner will defend it on video).
5. `/checkpoint` only when the user asks (it commits). Never push.

Do not invent requirements beyond the spec. If something is ambiguous, ask.

## Commands
```bash
docker run -d --name pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:17
docker run -d --name rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4-management
dotnet ef migrations add Initial -p src/TaskApi
dotnet run --project src/TaskApi
dotnet test
```

## Gotchas
- Testcontainers requires a running Docker daemon.
- In the test, declare a queue and bind it to `task.events`/`task.completed` BEFORE calling `/complete`,
  otherwise the message goes to an exchange with no queues and is lost.
- `WebApplicationFactory<Program>` needs `public partial class Program;` (or `{}`).
- xUnit v3: `IAsyncLifetime` methods return `ValueTask`.
- `xmin` is a system column: no migration column is created for `Version`, that is expected.
- Kit hook `post-edit-format` runs `dotnet format` on every edited .cs file — expected, not an error.
