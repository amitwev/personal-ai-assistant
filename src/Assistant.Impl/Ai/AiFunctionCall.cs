namespace Assistant.Impl.Ai;

/// <summary>
/// The tool name and arguments carried on one tool call.
/// </summary>
/// <param name="Name">The tool's name, matching an <see cref="AiFunctionDefinition"/> sent on the request.</param>
/// <param name="Arguments">
/// The model's arguments, as a JSON object serialised to a string rather than nested -- the
/// OpenAI-compatible wire format's own shape, not a choice this project made.
/// </param>
internal sealed record AiFunctionCall(string Name, string Arguments);
