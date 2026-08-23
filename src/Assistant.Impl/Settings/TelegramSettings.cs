using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the Telegram notifier.
/// </summary>
public sealed class TelegramSettings : IValidatableConfig
{
    /// <summary>
    /// The bot token issued by BotFather.
    /// </summary>
    public required string BotToken { get; init; }

    /// <summary>
    /// The chat the assistant reports to.
    /// </summary>
    public required long OwnerChatId { get; init; }

    /// <summary>
    /// The API base address, or null for the real Telegram API.
    /// </summary>
    /// <value>
    /// F4b's tests point this at a stub container. Optional, so it is not validated: absent
    /// means production.
    /// </value>
    public string? BaseUrl { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TelegramSettings)}.{nameof(BotToken)} is missing or empty.");
        }

        if (OwnerChatId == 0)
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TelegramSettings)}.{nameof(OwnerChatId)} is missing or zero.");
        }
    }
}
