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
}
