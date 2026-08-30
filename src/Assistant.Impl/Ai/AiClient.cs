using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat endpoint with the owner's text and the system prompt, and
/// returns the model's answer.
/// </summary>
/// <param name="api">The Refit client for the chat endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
internal sealed class AiClient(
    IAiApi api, SystemPrompt prompt, AiSettings settings) : IAiClient
{
    /// <inheritdoc/>
    public async Task<Result<string>> AskAsync(string userText, CancellationToken ct)
    {
        var response = await api.AskAsync(
            new AiRequest(
                settings.Model,
                [new AiMessage("system", prompt.Build()),
                 new AiMessage("user", userText)],
                settings.MaxTokens),
            ct);

        return Result<string>.Success(response.Choices[0].Message.Content!);
    }
}
