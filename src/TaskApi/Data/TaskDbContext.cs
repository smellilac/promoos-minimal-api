using Microsoft.EntityFrameworkCore;

namespace TaskApi.Data;

public sealed class TaskDbContext(DbContextOptions<TaskDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One entity — apply its configuration explicitly rather than scanning the assembly.
        modelBuilder.ApplyConfiguration(new TaskItemConfiguration());
    }
}
