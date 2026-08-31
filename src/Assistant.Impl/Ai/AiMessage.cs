namespace Assistant.Impl.Ai;

/// <summary>
/// One turn in the chat API's conversation, on either side of the wire.
/// </summary>
/// <param name="Role">
/// Who is speaking: <c>system</c>, <c>user</c>, or <c>assistant</c>.
/// </param>
/// <param name="Content">
/// What was said, or <see langword="null"/> on a response that carries only tool calls.
/// </param>
/// <param name="ToolCalls">
/// The tool calls the model asked for, or <see langword="null"/> on a request message, or on a
/// response that answered with <paramref name="Content"/> instead.
/// </param>
internal sealed record AiMessage(string Role, string? Content, IReadOnlyList<AiToolCall>? ToolCalls = null);
