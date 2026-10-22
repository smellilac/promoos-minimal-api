namespace TaskApi;

// Create accepts a DTO, never the TaskItem entity — so the client cannot set Id,
// IsCompleted, CreatedAt, CompletedAt or Version (invariant 2).
// Title is nullable so we can tell "missing" from "empty" and return 400 for both;
// Priority is nullable so an omitted value defaults to Medium (spec default).
public sealed record CreateTaskRequest(string? Title, Priority? Priority);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Priority Priority);

// Message contract published to RabbitMQ when a task is completed.
// Serialized camelCase with Priority as a string → { "taskId", "title", "completedAt", "priority" }.
public sealed record TaskCompletedEvent(
    Guid TaskId,
    string Title,
    DateTimeOffset CompletedAt,
    Priority Priority);
