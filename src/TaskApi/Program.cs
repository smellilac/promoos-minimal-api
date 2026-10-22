using Microsoft.EntityFrameworkCore;
using TaskApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Single clock source injected everywhere, so timestamps are UTC and testable (FakeTimeProvider).
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// HACK: не успел, доделал бы так: в проде миграции не в стартапе, а отдельным шагом
// деплоя (dotnet ef migrations bundle / dotnet ef database update в CI/CD) — чтобы
// несколько инстансов не гонялись за MigrateAsync и чтобы схему можно было откатывать
// независимо от выката приложения.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/", () => "Hello World!");

app.Run();
