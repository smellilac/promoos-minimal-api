using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaskApi.Data;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        // Store the enum by name ("Low"/"Medium"/"High") so the column is self-describing
        // and matches the string priority in the RabbitMQ contract.
        builder.Property(t => t.Priority)
            .HasConversion<string>()
            .HasMaxLength(16);

        // Optimistic concurrency via PostgreSQL's xmin system column (ADR 001).
        // IsRowVersion() marks Version as the concurrency token; Npgsql binds a uint
        // row-version to xmin, which already exists on every row — so EF adds it to the
        // UPDATE ... WHERE clause but generates NO column for it in the migration.
        builder.Property(t => t.Version)
            .IsRowVersion();
    }
}
