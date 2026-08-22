using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>
/// Persistence operations for tasks.
/// </summary>
/// <remarks>
/// Methods are named by intent rather than exposing a composable query, so each one can be
/// backed by an index built for it. Results are always materialised.
/// </remarks>
public interface ITaskRepository
{
    /// <summary>
    /// Adds a new task.
    /// </summary>
    /// <param name="task">The task to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task AddAsync(ReminderTask task, CancellationToken ct);

    /// <summary>
    /// Returns the task with the given identifier.
    /// </summary>
    /// <param name="id">The identifier to look for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The task, or <see langword="null"/> when no row carries that identifier. The result is
    /// not change-tracked: mutations go through the task service, never by writing to this
    /// object and expecting it to be saved.
    /// </returns>
    Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns pending tasks that are due and whose reminder has not yet been delivered.
    /// </summary>
    /// <param name="asOfUtc">The instant to treat as "now".</param>
    /// <param name="limit">Maximum number of tasks to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tasks ordered by due time, oldest first. There is no lower bound on the due time, so a
    /// task missed during an outage is still returned once the process is running again.
    /// </returns>
    Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct);
}
