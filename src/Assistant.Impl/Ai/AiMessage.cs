namespace Assistant.Impl.Ai;

/// <summary>
/// One turn in the chat API's conversation, on either side of the wire.
/// </summary>
/// <param name="Role">
/// Who is speaking: <c>system</c>, <c>user</c>, or <c>assistant</c>.
/// </param>
/// <param name="Content">
/// What was said, or <see langword="null"/> on a response that carries only tool calls (F9b) —
/// harmless now, since F9a never sends or reads a null one.
/// </param>
internal sealed record AiMessage(string Role, string? Content);
