using Refit;

namespace Assistant.Impl.Ai;

/// <summary>
/// The OpenAI-compatible chat endpoint, reachable at any provider that speaks it.
/// </summary>
/// <remarks>
/// Named for the wire format, not a vendor: OpenRouter, OpenAI, Groq and a local Ollama all
/// serve this same shape, so moving providers is a change to
/// <see cref="Assistant.Impl.Settings.AiSettings.BaseUrl"/> and nothing in this interface.
/// </remarks>
internal interface IAiApi
{
    /// <summary>
    /// Asks the chat endpoint for its response to the given request.
    /// </summary>
    /// <param name="request">The model, conversation and token limit to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provider's response, including every candidate answer it offered.</returns>
    [Post("/chat/completions")]
    Task<AiResponse> AskAsync([Body] AiRequest request, CancellationToken ct);
}
