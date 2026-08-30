namespace Assistant.Impl.Ai;

/// <summary>
/// A request to the OpenAI-compatible chat API, which OpenRouter, OpenAI, Groq and a local
/// Ollama all serve.
/// </summary>
/// <param name="Model">The model slug to request, such as <c>anthropic/claude-sonnet-5</c>.</param>
/// <param name="Messages">The conversation so far, system prompt first.</param>
/// <param name="MaxTokens">The maximum number of tokens the model may return.</param>
internal sealed record AiRequest(
    string Model, IReadOnlyList<AiMessage> Messages, int MaxTokens);

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

/// <summary>
/// A response from the chat API, carrying every answer the model offered.
/// </summary>
/// <param name="Choices">
/// The model's candidate answers. Empty when the provider accepted the request but produced
/// nothing.
/// </param>
internal sealed record AiResponse(IReadOnlyList<AiChoice> Choices);

/// <summary>
/// One candidate answer within a response from the chat API.
/// </summary>
/// <param name="Message">The answer itself, in the same shape a request message takes.</param>
internal sealed record AiChoice(AiMessage Message);
