using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Services.Actions;

/// <summary>
/// Moves a task's due time forward in response to its Schedule button being tapped.
/// </summary>
/// <param name="taskService">The single writer for tasks.</param>
/// <param name="timeProvider">
/// The current instant. <see cref="TaskActions.PlusOneHour"/> is added to this, never to the
/// task's own <see cref="ReminderTask.DueAt"/> -- see <see cref="ExecuteAsync"/>.
/// </param>
internal sealed class ScheduleAction(ITaskService taskService, TimeProvider timeProvider) : ITaskAction
{
    /// <inheritdoc/>
    public TaskActionDefinition Definition => TaskActions.Schedule;

    /// <inheritdoc/>
    /// <remarks>
    /// Only <see cref="TaskActions.PlusOneHour"/> is understood; any other <c>argument</c> -- a
    /// future preset this slice does not yet implement, or a corrupted callback string -- is
    /// refused with <see cref="ErrorCode.TaskActionArgumentUnrecognized"/> rather than guessed at.
    /// <para>
    /// One hour is added to <see cref="TimeProvider"/>'s current instant, never to the task's own
    /// <see cref="ReminderTask.DueAt"/>: a reminder that has already fired carries a due time in
    /// the past, and adding an hour to a past instant would still be in the past, or would fire
    /// again immediately -- neither of which is "one hour from now". Adding to the current UTC
    /// instant, rather than to a local wall-clock reading of it, also means this stays correct
    /// across a daylight-saving transition, where a wall-clock "+1 hour" can be zero or two real
    /// hours away. This is why this action needs no <see cref="ILocalTimeResolver"/>: it never
    /// reads or writes a wall-clock reading at all.
    /// </para>
    /// </remarks>
    public Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct) =>
        argument == TaskActions.PlusOneHour
            ? taskService.RescheduleAsync(taskId, timeProvider.GetUtcNow().AddHours(1), ct)
            : Task.FromResult(Result<ReminderTask>.Failure(ErrorCode.TaskActionArgumentUnrecognized));
}
