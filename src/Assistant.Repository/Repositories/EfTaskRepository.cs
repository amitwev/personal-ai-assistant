using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository.Repositories;

/// <summary>
/// Entity Framework implementation of <see cref="ITaskRepository"/>.
/// </summary>
/// <param name="db">The assistant's database context.</param>
/// <remarks>
/// Internal by design: callers resolve <see cref="ITaskRepository"/> from the container, so no
/// project outside this assembly names an Entity Framework type. Each method saves immediately.
/// There is no unit of work because every caller writes one task at a time, and introducing one
/// before a caller needs it would be a guess.
/// </remarks>
internal sealed class EfTaskRepository(AssistantDbContext db) : ITaskRepository
{
    /// <inheritdoc/>
    public async Task AddAsync(ReminderTask task, CancellationToken ct)
    {
        db.ReminderTasks.Add(task);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct) =>
        db.ReminderTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
}
