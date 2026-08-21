using Assistant.Contracts;
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
    /// Finds a task by identifier.
    /// </summary>
    /// <param name="id">Identifier to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task, or <see langword="null"/> when no task has that identifier.</returns>
    Task<ReminderTask?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Adds a new task.
    /// </summary>
    /// <param name="task">The task to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task AddAsync(ReminderTask task, CancellationToken ct);

    /// <summary>
    /// Persists changes made to a previously loaded task.
    /// </summary>
    /// <param name="task">The task to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task UpdateAsync(ReminderTask task, CancellationToken ct);

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

    /// <summary>
    /// Returns pending tasks matching a filter.
    /// </summary>
    /// <param name="filter">Which tasks to return.</param>
    /// <param name="asOfUtc">The instant to treat as "now" when resolving relative filters.</param>
    /// <param name="limit">Maximum number of tasks to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tasks ordered by due time, with undated tasks last.</returns>
    Task<IReadOnlyList<ReminderTask>> QueryAsync(
        TaskFilter filter, DateTimeOffset asOfUtc, int limit, CancellationToken ct);

    /// <summary>
    /// Counts pending tasks that have no due time.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of pending tasks with no deadline.</returns>
    Task<int> CountOpenWithoutDueDateAsync(CancellationToken ct);
}
