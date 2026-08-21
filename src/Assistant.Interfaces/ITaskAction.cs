using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// One behaviour reachable from a message button.
/// </summary>
/// <remarks>
/// Adding a button means adding an implementation of this interface with a new <see cref="Key"/>.
/// No existing type changes.
/// </remarks>
public interface ITaskAction
{
    /// <summary>
    /// The token identifying this action inside a button payload.
    /// </summary>
    /// <value>Lowercase, no colons, kept short because the payload budget is 64 bytes.</value>
    string Key { get; }

    /// <summary>
    /// Applies the action to a task.
    /// </summary>
    /// <param name="taskId">Identifier of the task the button belongs to.</param>
    /// <param name="argument">
    /// The action's optional argument from the payload, such as a snooze duration.
    /// <see langword="null"/> when the payload carried none.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The text to show the user as a short confirmation, and whether the originating message's
    /// buttons should be removed.
    /// </returns>
    Task<(Result Result, string UserMessage, bool RemoveButtons)> ExecuteAsync(
        Guid taskId, string? argument, CancellationToken ct);
}
