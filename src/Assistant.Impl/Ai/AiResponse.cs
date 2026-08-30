namespace Assistant.Impl.Ai;

/// <summary>
/// A response from the chat API, carrying every answer the model offered.
/// </summary>
/// <param name="Choices">
/// The model's candidate answers. Empty when the provider accepted the request but produced
/// nothing.
/// </param>
internal sealed record AiResponse(IReadOnlyList<AiChoice> Choices);
