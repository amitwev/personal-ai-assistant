namespace Assistant.Impl.Ai;

/// <summary>
/// One invocation the model asked for, carried on an assistant message's <c>tool_calls</c> array.
/// </summary>
/// <param name="Id">
/// The provider's identifier for this call. Unused today -- nothing sends a follow-up turn that
/// would need to echo it back -- and dropped rather than carried onto <c>ToolCall</c>, the
/// public shape <see cref="AiClient"/> returns.
/// </param>
/// <param name="Type">Always <c>function</c> -- the only tool call type this wire format defines.</param>
/// <param name="Function">The tool name and arguments the model chose.</param>
internal sealed record AiToolCall(string Id, string Type, AiFunctionCall Function);
