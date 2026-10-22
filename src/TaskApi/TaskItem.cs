namespace TaskApi;

public sealed class TaskItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Null on create, set only when the task is completed (spec invariant).
    public DateTimeOffset? CompletedAt { get; set; }

    public Priority Priority { get; set; }

    // Optimistic-concurrency token backed by PostgreSQL's xmin system column (ADR 001).
    // Npgsql maps this uint row-version onto xmin, so no physical column is created for it.
    public uint Version { get; set; }
}

public enum Priority
{
    Low,
    Medium,
    High,
}
