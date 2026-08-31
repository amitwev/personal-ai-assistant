namespace Assistant.Contracts;

/// <summary>
/// One tool invocation the model asked for, parsed out of its answer.
/// </summary>
/// <param name="Name">
/// The tool's name, matching an <c>IAssistantTool.Name</c> sent on the request.
/// </param>
/// <param name="ArgumentsJson">
/// The model's arguments, as the raw JSON object text the wire carried. Binding it to a
/// specific tool's own request shape, such as <see cref="CreateTaskRequest"/>, is the calling
/// tool's job, not the transport's.
/// </param>
public sealed record ToolCall(string Name, string ArgumentsJson);
