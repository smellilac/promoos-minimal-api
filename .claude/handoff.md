# Handoff — TaskApi test assignment

## Completed
- [x] Stage 1 (init): project brief + spec + ADRs + `.gitignore` as the first commit (`init:`, a9c324d).
- [x] Stage 1 cont. (scaffold): `TaskApi.sln`, `src/TaskApi` web project (net9.0) + packages
      (Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4, EFCore.Design 9.0.21, RabbitMQ.Client 7.2.2),
      `tests/TaskApi.Tests` (xUnit v3 4.0.2 + Mvc.Testing 9.0.21 + Testcontainers PostgreSql/RabbitMq 4.16.0).
- [x] `Directory.Build.props`: LangVersion 13.0, Nullable + ImplicitUsings enabled.
- [x] `.mcp.json`: cwm-roslyn-navigator MCP server.
- [x] Placeholder `SmokeTest` so the test project is wired up. `dotnet build TaskApi.sln` is green.

## Pending (next steps, in order)
- [ ] Stage 2: `TaskItem` entity + `Priority` enum, `TaskDbContext`, `IEntityTypeConfiguration`,
      `uint Version` (.IsRowVersion → xmin), initial EF migration.
- [ ] Stage 3 (TDD): 4 endpoints + validation (Title not blank, ≤200 after Trim → ValidationProblem) + 409 concurrency.
- [ ] Stage 4 (TDD): RabbitMQ publisher via `Channel<T>` + `BackgroundService`, publish only after SaveChanges,
      broker failure never fails request.
- [ ] Stage 5: graceful shutdown (Writer.Complete → drain ≤5s → CloseAsync).
- [ ] Stage 6: verify + README. Stage 7 (bonus): pagination.
- Run `/plan` before each stage per CLAUDE.md workflow.

## Learned / environment
- Only the **.NET 10 SDK + runtime (10.0.112 / 10.0.12)** is installed — no net9.0 runtime.
  `dotnet build` of net9.0 works, but `dotnet test` / running the net9.0 app fails:
  `dotnet test` errors "Testing with VSTest ... no longer supported ... on .NET 10 SDK";
  `dotnet run` fails "framework 'Microsoft.NETCore.App' 9.0.0 not found".
  To run tests locally, install the .NET 9 runtime, OR add a `global.json` pinning a 9 SDK,
  OR set `<RollForward>Major</RollForward>` + opt into the new `dotnet test` MTP experience.
  Not blocking scaffolding (build is clean); must resolve before Stage 3 TDD needs a running test host.

## Context
- Branch: master (main is the PR target) | Scaffold commit: see latest `git log`
- Git identity set locally: dmitrii / dimatega@gmail.com
