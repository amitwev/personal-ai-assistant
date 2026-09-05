using Assistant.Contracts;
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>
/// The only type permitted to change a task.
/// </summary>
/// <remarks>
/// Models carry no behaviour, so the rules need one owner instead. Jobs, tool handlers and button
/// actions all call this; none of them touches a repository directly. The interface grows a method
/// per feature that needs one — it is a data-access surface, not a behaviour seam.
/// </remarks>
public interface ITaskService
{
    /// <summary>
    /// Records that the reminder for a task has been delivered.
    /// </summary>
    /// <param name="id">The task whose reminder was delivered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success, or the reason it was refused. Refused when no task carries the identifier, or when
    /// the task has no due time and therefore had no reminder to deliver.
    /// </returns>
    Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns pending tasks that are due and whose reminder has not yet been delivered.
    /// </summary>
    /// <param name="limit">Maximum number of tasks to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tasks ordered by due time, oldest first. The service decides what "now" means: callers
    /// never pass an instant, so a job's notion of due time cannot drift from the rest of the
    /// assistant's.
    /// </returns>
    Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(int limit, CancellationToken ct);

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    /// <param name="id">The task to complete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success, or the reason it was refused. Refused when no task carries the identifier, or
    /// when the task has already been completed.
    /// </returns>
    /// <remarks>
    /// A second call on an already-completed task is refused with
    /// <see cref="ErrorCode.TaskAlreadyCompleted"/> rather than repeating the write: the row is
    /// left exactly as the first call set it, so <see cref="ReminderTask.CompletedAt"/> always
    /// carries the instant of the first completion, never a later one.
    /// </remarks>
    Task<Result> CompleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Creates a new task.
    /// </summary>
    /// <param name="request">The captured request, carrying the task's title.</param>
    /// <param name="dueAtUtc">
    /// The task's due instant in UTC, already resolved from the request's local-time text against
    /// the configured zone -- or <see langword="null"/> when the request gave no time. Resolving
    /// is the caller's job: this service has no zone of its own to resolve against, and the
    /// resolver's own guard clauses (<see cref="ErrorCode.DueTimeInPast"/>,
    /// <see cref="ErrorCode.DueTimeTooFarAhead"/>) must already have passed before this method is
    /// ever called, so nothing is persisted on a time the resolver would have refused.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The stored task, with a freshly generated identifier and <see cref="ReminderStatus.Pending"/>
    /// status. This method never fails: there is no precondition on a create the way there is on
    /// looking an existing task up by identifier.
    /// </returns>
    Task<Result<ReminderTask>> CreateAsync(
        CreateTaskRequest request, DateTimeOffset? dueAtUtc, CancellationToken ct);
}

