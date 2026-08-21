namespace Assistant.Interfaces;

/// <summary>
/// One capability the language model may invoke.
/// </summary>
/// <remarks>
/// Adding a capability means adding an implementation of this interface. Registration is by
/// convention, so no existing type changes.
/// </remarks>
public interface IAssistantTool
{
    /// <summary>
    /// The tool's name as exposed to the model.
    /// </summary>
    /// <value>Lowercase snake case, for example <c>create_task</c>.</value>
    string Name { get; }

    /// <summary>
    /// What the tool does, written for the model rather than for a developer.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The JSON Schema describing the tool's parameters.
    /// </summary>
    /// <value>A JSON object schema serialised as text.</value>
    string ParametersJsonSchema { get; }

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="argumentsJson">The model's arguments, as a JSON object.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Text to return to the model as the tool's result. On rejection this is the explanation,
    /// so the model can ask the user a follow-up question rather than failing silently.
    /// </returns>
    Task<string> InvokeAsync(string argumentsJson, CancellationToken ct);
}
