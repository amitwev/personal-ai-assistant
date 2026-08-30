using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the chat-completions endpoint the assistant reaches for an answer.
/// </summary>
/// <remarks>
/// One provider serves the whole assistant. Unlike <see cref="TelegramSettings.BaseUrl"/>,
/// <see cref="BaseUrl"/> here is required: there is no single "the" chat-completions provider
/// the way there is a single real Telegram API, so a value must always be supplied, and
/// <c>appsettings.json</c> ships OpenRouter's address as a changeable default (decision 2).
/// </remarks>
public sealed class AiSettings : IValidatableConfig
{
    /// <summary>
    /// The API key sent as a bearer token on every request.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// The chat-completions endpoint's base address, such as
    /// <c>https://openrouter.ai/api/v1</c>.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// The model slug to request, such as <c>anthropic/claude-sonnet-5</c>.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// The maximum number of tokens the model may return.
    /// </summary>
    public required int MaxTokens { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(ApiKey)} is missing or empty.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(BaseUrl)} is '{BaseUrl}', which is not an "
                + "absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(Model)} is missing or empty.");
        }

        if (MaxTokens <= 0)
        {
            throw new ConfigurationErrorsException(
                $"{nameof(AiSettings)}.{nameof(MaxTokens)} is {MaxTokens}, which is not "
                + "positive.");
        }
    }
}
