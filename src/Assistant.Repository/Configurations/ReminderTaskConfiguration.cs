using Assistant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Assistant.Repository.Configurations;

/// <summary>
/// Maps <see cref="ReminderTask"/> onto the <c>reminder_tasks</c> table.
/// </summary>
internal sealed class ReminderTaskConfiguration : IEntityTypeConfiguration<ReminderTask>
{
    /// <summary>
    /// Configures the table, its columns, and its check constraints.
    /// </summary>
    /// <param name="builder">The builder for <see cref="ReminderTask"/>.</param>
    public void Configure(EntityTypeBuilder<ReminderTask> builder)
    {
        builder.ToTable("reminder_tasks", t =>
        {
            t.HasCheckConstraint("ck_reminder_tasks_status_known", "status <> 0");
            t.HasCheckConstraint(
                "ck_reminder_tasks_sent_requires_due",
                "reminder_sent_at IS NULL OR due_at IS NOT NULL");
            t.HasCheckConstraint(
                "ck_reminder_tasks_completed_consistency",
                "(status = 2) = (completed_at IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Title).HasColumnName("title").IsRequired().HasMaxLength(500);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.DueAt).HasColumnName("due_at").HasColumnType("timestamptz");
        builder.Property(x => x.ReminderSentAt)
            .HasColumnName("reminder_sent_at")
            .HasColumnType("timestamptz");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamptz");

        builder.HasIndex(x => x.DueAt)
            .HasDatabaseName("idx_tasks_due_pending")
            .HasFilter("status = 1 AND reminder_sent_at IS NULL");
    }
}
