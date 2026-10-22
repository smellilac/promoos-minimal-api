using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.Messaging;

namespace TaskApi;

public static class TaskEndpoints
{
    private const int MaxTitleLength = 200;

    // Explicit wiring (ADR 006): one group, called from Program.cs — no reflection discovery.
    public static void MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tasks");

        group.MapPost("/", CreateTask);
        group.MapGet("/", GetTasks);
        group.MapPut("/{id:guid}/complete", CompleteTask);
        group.MapDelete("/{id:guid}", DeleteTask);
    }

    private static async Task<Results<Created<TaskResponse>, ValidationProblem>> CreateTask(
        CreateTaskRequest request,
        TaskDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        // Invariant 1: reject null/empty/whitespace and anything longer than 200 chars after Trim.
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required and cannot be empty."],
            });
        }

        var title = request.Title.Trim();
        if (title.Length > MaxTitleLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = [$"Title must be at most {MaxTitleLength} characters."],
            });
        }

        // Invariant 9: "Urgent" (an unknown string) already fails at JSON binding → 400.
        // A numeric value like 99 binds silently to an undefined enum, so check it explicitly here.
        if (request.Priority is { } priority && !Enum.IsDefined(priority))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["priority"] = ["Priority must be one of: Low, Medium, High."],
            });
        }

        // Invariant 2 & 8: CompletedAt stays null on create; timestamps are UTC from TimeProvider.
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            IsCompleted = false,
            CreatedAt = timeProvider.GetUtcNow(),
            CompletedAt = null,
            Priority = request.Priority ?? Priority.Medium,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/tasks/{task.Id}", ToResponse(task));
    }

    private static async Task<Ok<IReadOnlyList<TaskResponse>>> GetTasks(
        TaskDbContext db,
        CancellationToken ct)
    {
        // HACK: не успел, доделал бы так: добавил бы пагинацию через query-параметры
        // ?page&pageSize (или keyset по CreatedAt+Id для стабильного порядка) и возвращал бы
        // PagedResponse с total/hasMore. Спека просит вернуть ВСЕ задачи, поэтому пока без лимита;
        // AsNoTracking — это read-only путь, менять сущности не нужно.
        var tasks = await db.Tasks
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .Select(t => new TaskResponse(
                t.Id, t.Title, t.IsCompleted, t.CreatedAt, t.CompletedAt, t.Priority))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<TaskResponse>>(tasks);
    }

    private static async Task<Results<Ok<TaskResponse>, NotFound, ProblemHttpResult>> CompleteTask(
        Guid id,
        TaskDbContext db,
        TimeProvider timeProvider,
        ITaskEventPublisher publisher,
        CancellationToken ct)
    {
        // Tracked read: we intend to update, and EF will add xmin to the UPDATE ... WHERE clause.
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
        {
            return TypedResults.NotFound();
        }

        // Invariant 4, case A: already completed — the other request committed before us.
        if (task.IsCompleted)
        {
            return Conflict("Task is already completed.");
        }

        task.IsCompleted = true;
        task.CompletedAt = timeProvider.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Invariant 4, case B: a real race — both read it incomplete, xmin no longer matches.
            return Conflict("Task was modified concurrently and is already completed.");
        }

        // Invariant 3 / ADR 003: publish ONLY after a successful save — otherwise a DB failure
        // would leave subscribers believing in a completion that never persisted. Enqueue is
        // non-blocking (ADR 002): the response doesn't wait on RabbitMQ.
        // HACK: не успел, доделал бы так: транзакционный Outbox — писал бы событие в таблицу
        // outbox в той же транзакции, что и обновление задачи, а фоновый relay публиковал бы и
        // помечал отправленным. Сейчас между SaveChanges и Enqueue есть окно: если процесс умрёт,
        // событие потеряется (at-most-once вместо at-least-once).
        publisher.Enqueue(new TaskCompletedEvent(task.Id, task.Title, task.CompletedAt!.Value, task.Priority));

        return TypedResults.Ok(ToResponse(task));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteTask(
        Guid id,
        TaskDbContext db,
        CancellationToken ct)
    {
        // Hard delete in one round-trip; no entity load needed.
        var deleted = await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    // 409 as an RFC 9457 ProblemDetails, consistent with the global error contract (invariant 10).
    private static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict);

    private static TaskResponse ToResponse(TaskItem task) =>
        new(task.Id, task.Title, task.IsCompleted, task.CreatedAt, task.CompletedAt, task.Priority);
}
