using Assistant.Models;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository;

/// <summary>
/// Entity Framework context for the assistant's persisted tables.
/// </summary>
/// <param name="options">Provider and connection configuration.</param>
/// <remarks>
/// Internal to this assembly by design: no other project names a context or a
/// <see cref="DbSet{TEntity}"/> directly. Callers reach persistence through the repository
/// interfaces registered by <see cref="RepositoryServiceCollectionExtensions"/>.
/// </remarks>
internal sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    /// <summary>
    /// The <c>reminder_tasks</c> table.
    /// </summary>
    public DbSet<ReminderTask> ReminderTasks => Set<ReminderTask>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssistantDbContext).Assembly);
}
