using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat endpoint with the owner's text and the system prompt, and
/// returns the model's answer.
/// </summary>
/// <param name="api">The Refit client for the chat endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
/// <param name="logger">Where a provider failure is recorded.</param>
internal sealed class AiClient(
    IAiApi api, SystemPrompt prompt, AiSettings settings,
    ILogger<AiClient> logger) : IAiClient
{
    /// <inheritdoc/>
    public async Task<Result<string>> AskAsync(string userText, CancellationToken ct)
    {
        AiResponse response;
        try
        {
            response = await api.AskAsync(
                new AiRequest(
                    settings.Model,
                    [new AiMessage("system", prompt.Build()),
                     new AiMessage("user", userText)],
                    settings.MaxTokens),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reaching the chat model failed.");
            return Result<string>.Failure(ErrorCode.ModelUnavailable);
        }

        var answer = response.Choices.FirstOrDefault()?.Message.Content;

        if (string.IsNullOrWhiteSpace(answer))
        {
            logger.LogError("The chat model returned no answer.");
            return Result<string>.Failure(ErrorCode.ModelReturnedNoAnswer);
        }

        return Result<string>.Success(answer);
    }
}
