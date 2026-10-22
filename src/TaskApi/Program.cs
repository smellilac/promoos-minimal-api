using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TaskApi;
using TaskApi.Data;
using TaskApi.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Single clock source injected everywhere, so timestamps are UTC and testable (FakeTimeProvider).
builder.Services.AddSingleton(TimeProvider.System);

// Messaging (ADR 002): one RabbitMqPublisher instance is both the queue the endpoint writes to
// (ITaskEventPublisher) and the background reader that publishes to RabbitMQ (IHostedService).
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<ITaskEventPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqPublisher>());

// Give the host 10s to stop so our 5s RabbitMQ drain (ADR 004, invariant 7) isn't cut short
// by the 5s default shutdown timeout.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(10));

// Invariant 9: camelCase JSON, enums serialized as strings ("Low"/"Medium"/"High").
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Invariant 10: unhandled errors become RFC 9457 ProblemDetails.
builder.Services.AddProblemDetails();

var app = builder.Build();

// Invariant 10: turn exceptions into ProblemDetails. BadHttpRequestException (malformed JSON,
// unparseable enum/route values) carries its own 400 — keep that status instead of masking it
// as 500, so bad input stays a client error.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});

// Invariant 10: give empty-body error responses (e.g. bare 404) a ProblemDetails body too.
app.UseStatusCodePages();

// HACK: не успел, доделал бы так: в проде миграции не в стартапе, а отдельным шагом
// деплоя (dotnet ef migrations bundle / dotnet ef database update в CI/CD) — чтобы
// несколько инстансов не гонялись за MigrateAsync и чтобы схему можно было откатывать
// независимо от выката приложения.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await db.Database.MigrateAsync();
}

app.MapTaskEndpoints();

app.Run();

// Exposes the implicit Program class so WebApplicationFactory<Program> can boot it in tests.
public partial class Program;
