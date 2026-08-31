namespace Assistant.Interfaces;

/// <summary>
/// One capability the chat model may invoke.
/// </summary>
/// <remarks>
/// Carries only what a tool definition needs on the wire request. No execution member exists
/// yet: <see cref="ITaskService"/> has no method to create a task until F10, so an
/// <c>ExecuteAsync</c> here would have nothing real to call. Adding one then is a deliberate
/// modification to this interface, not an extension by a new class.
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
}
