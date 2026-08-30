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
