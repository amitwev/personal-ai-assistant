using Assistant.Contracts;

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
}
