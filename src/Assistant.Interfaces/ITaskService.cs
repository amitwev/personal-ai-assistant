using Assistant.Contracts;
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>
/// The single writer for tasks. Every mutation in the system goes through here.
/// </summary>
/// <remarks>
/// Because models carry no behaviour, this is the only place task invariants are enforced.
/// Nothing else — no job, no tool, no button handler — may mutate a task or call a repository
/// write directly.
/// </remarks>
public interface ITaskService
{
    /// <summary>
    /// Creates a task, resolving its due time from local text.
    /// </summary>
    /// <param name="request">What to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The created task on success; a failure carrying <see cref="ErrorCode.TimeInPast"/>,
    /// <see cref="ErrorCode.TimeTooFarAhead"/>, or <see cref="ErrorCode.TimeUnparseable"/> when
    /// the requested due time is rejected.
    /// </returns>
    Task<(Result Result, ReminderTask? Task)> CreateAsync(CreateTaskRequest request, CancellationToken ct);

    /// <summary>
    /// Applies changes to an existing task.
    /// </summary>
    /// <param name="request">What to change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the change was rejected.</returns>
    /// <remarks>Changing the due time clears the reminder marker, so the task fires again.</remarks>
    Task<Result> UpdateAsync(UpdateTaskRequest request, CancellationToken ct);

    /// <summary>
    /// Marks a task complete.
    /// </summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success — including when the task was already complete, so a button can be pressed twice
    /// safely. A failure carrying <see cref="ErrorCode.TaskCancelled"/> when the task was cancelled.
    /// </returns>
    Task<Result> CompleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Marks a task cancelled without completing it.
    /// </summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the cancellation was rejected.</returns>
    Task<Result> CancelAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Moves a task's due time forward and re-arms its reminder.
    /// </summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="duration">How far forward to move the due time. Must be positive.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the snooze was rejected.</returns>
    /// <remarks>
    /// Snoozing clears the reminder-sent marker and resets the delivery attempt count, so the
    /// task fires again at its new due time. Snoozing measures from the current time, not from
    /// the old due time, so snoozing an overdue task by an hour means an hour from now.
    /// See <see cref="RescheduleAsync"/> to set an absolute time instead.
    /// </remarks>
    Task<Result> SnoozeAsync(Guid id, TimeSpan duration, CancellationToken ct);

    /// <summary>
    /// Sets a task's due time to a specific instant and re-arms its reminder.
    /// </summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="newDueAtUtc">The new due time, in UTC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure explaining why the reschedule was rejected.</returns>
    Task<Result> RescheduleAsync(Guid id, DateTimeOffset newDueAtUtc, CancellationToken ct);

    /// <summary>
    /// Records that a task's reminder has been delivered.
    /// </summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success, or a failure carrying <see cref="ErrorCode.TaskHasNoDueTime"/> when the task has
    /// no due time and therefore no reminder to have delivered.
    /// </returns>
    /// <remarks>
    /// Called only after the message has actually been sent. Marking before sending would lose a
    /// reminder whenever the send fails.
    /// </remarks>
    Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Records a failed delivery attempt.
    /// </summary>
    /// <param name="id">Identifier of the task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a failure when no such task exists.</returns>
    /// <remarks>
    /// Once the attempt count reaches three the task is treated as undeliverable and is no longer
    /// returned by the due-reminder query, so a permanently failing send cannot loop forever.
    /// </remarks>
    Task<Result> RecordDeliveryFailureAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lists tasks matching a filter.
    /// </summary>
    /// <param name="request">Which tasks to return, and how many.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matching tasks, ordered by due time with undated tasks last.</returns>
    Task<IReadOnlyList<ReminderTask>> QueryAsync(ListTasksRequest request, CancellationToken ct);
}
