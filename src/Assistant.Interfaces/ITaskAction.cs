using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// One action an inline button's tap can perform on a task.
/// </summary>
/// <remarks>
/// Resolved by <see cref="Key"/> against the callback codec's decoded action segment. A caller
/// that finds no implementation whose key matches produces a polite reply rather than throwing,
/// per spec 6.4. <c>DoneAction</c> is the first implementation; snooze, reschedule and edit
/// actions follow at F11, each adding one more implementation rather than changing this one.
/// </remarks>
public interface ITaskAction
{
    /// <summary>
    /// The action's key, as carried on the wire inside the callback codec.
    /// </summary>
    /// <value>Lowercase, for example <c>done</c>.</value>
    string Key { get; }

    /// <summary>
    /// Performs the action against the given task.
    /// </summary>
    /// <param name="taskId">The task the button referred to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    Task<Result> ExecuteAsync(Guid taskId, CancellationToken ct);
}
