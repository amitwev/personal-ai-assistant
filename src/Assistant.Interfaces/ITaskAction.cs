using Assistant.Contracts;
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>
/// One action an inline button's tap can perform on a task.
/// </summary>
/// <remarks>
/// Resolved by matching <see cref="Definition"/>'s <see cref="TaskActionDefinition.Key"/> against
/// the callback codec's decoded action segment. A caller that finds no implementation whose key
/// matches produces a polite reply rather than throwing, per spec 6.4. <c>DoneAction</c> is the
/// first implementation; <c>ScheduleAction</c> follows at F11-3, reading <c>argument</c> as a
/// preset key such as <c>+1h</c>. This interface is modified in place as each new capability
/// demands one: <see cref="ExecuteAsync"/> gained <c>argument</c> and a widened return here, at
/// F11-2, rather than a second interface being invented alongside it for actions that carry one.
/// </remarks>
public interface ITaskAction
{
    /// <summary>
    /// This action's entry in the shared catalogue.
    /// </summary>
    /// <value>Key, label and description all come from <see cref="TaskActions"/>.</value>
    TaskActionDefinition Definition { get; }

    /// <summary>
    /// Performs the action against the given task.
    /// </summary>
    /// <param name="taskId">The task the button referred to.</param>
    /// <param name="argument">
    /// The button's carried argument, decoded from the callback data's optional fourth segment,
    /// or an empty string when the action takes none. <c>DoneAction</c> ignores it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task, or the reason the action was refused.</returns>
    Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct);
}
