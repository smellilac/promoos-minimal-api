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
