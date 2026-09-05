using Assistant.Contracts;
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>
/// One capability the chat model may invoke.
/// </summary>
/// <remarks>
/// Carries both what a tool definition needs on the wire request and the one member that
/// actually carries it out. Adding <see cref="ExecuteAsync"/> was a deliberate modification to
/// this interface, made once <see cref="ITaskService"/> grew a create method to call (F10-1) --
/// not an extension by a new class. It is also not expected to be the last such modification: a
/// future tool returning many tasks, or none, cannot fill <see cref="Result{T}"/> of
/// <see cref="ReminderTask"/> either, so a second modification at F11 is an accepted, known cost
/// of this shape, not a surprise a later plan has to explain away.
/// </remarks>
public interface IAssistantTool
{
    /// <summary>
    /// The tool's name as sent on the wire request and echoed back on a tool call.
    /// </summary>
    /// <value>Lowercase snake case, for example <c>create_task</c>.</value>
    string Name { get; }

    /// <summary>
    /// What the tool does, written for the model rather than a developer.
    /// </summary>
    /// <value>A plain-language instruction telling the model when to call this tool.</value>
    string Description { get; }

    /// <summary>
    /// The JSON Schema object describing the tool's parameters.
    /// </summary>
    /// <value>Raw JSON text: a <c>type: object</c> schema with <c>properties</c> and <c>required</c>.</value>
    string ParametersJsonSchema { get; }

    /// <summary>
    /// Binds the model's raw arguments to this tool's own request shape and carries it out.
    /// </summary>
    /// <param name="argumentsJson">
    /// The model's arguments, as the raw JSON object text the wire carried. The model is not
    /// bound by <see cref="ParametersJsonSchema"/>: a required field may be absent, empty, or of
    /// the wrong shape, and the text itself may not parse as JSON at all.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The task this call created, or the reason it was refused:
    /// <see cref="ErrorCode.ToolArgumentsMalformed"/> when the arguments could not be parsed as
    /// a JSON object at all, <see cref="ErrorCode.ToolArgumentMissing"/> when a field this tool
    /// requires was absent or blank, or -- when a due time was given but could not be honoured --
    /// <see cref="ErrorCode.DueTimeUnparseable"/>, <see cref="ErrorCode.DueTimeInPast"/>, or
    /// <see cref="ErrorCode.DueTimeTooFarAhead"/>. Nothing is persisted on any failure path.
    /// </returns>
    Task<Result<ReminderTask>> ExecuteAsync(string argumentsJson, CancellationToken ct);
}
