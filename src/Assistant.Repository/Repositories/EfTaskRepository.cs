using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Repository.Repositories;

/// <summary>
/// Entity Framework implementation of <see cref="ITaskRepository"/>.
/// </summary>
/// <remarks>
/// Internal by design: callers resolve <see cref="ITaskRepository"/> from the container, so no
/// project outside this assembly names an Entity Framework type. Each method saves immediately.
/// There is no unit of work because every caller writes one task at a time, and introducing one
/// before a caller needs it would be a guess.
/// </remarks>
internal sealed class EfTaskRepository : ITaskRepository
{
    private readonly AssistantDbContext _db;

    /// <summary>
    /// Initialises the repository with the context it reads and writes through.
    /// </summary>
    /// <param name="db">The assistant's database context.</param>
    public EfTaskRepository(AssistantDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task AddAsync(ReminderTask task, CancellationToken ct)
    {
        _db.ReminderTasks.Add(task);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct) =>
        _db.ReminderTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
}
