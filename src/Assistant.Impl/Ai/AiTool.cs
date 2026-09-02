namespace Assistant.Impl.Ai;

/// <summary>
/// One tool definition offered to the model on a request, in the OpenAI-compatible
/// function-calling shape.
/// </summary>
/// <param name="Type">Always <c>function</c> -- the only tool type this wire format defines.</param>
/// <param name="Function">The tool's name, description and parameter schema.</param>
internal sealed record AiTool(string Type, AiFunctionDefinition Function);
