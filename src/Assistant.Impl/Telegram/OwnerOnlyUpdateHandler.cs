using Assistant.Impl.Scheduling;
using Assistant.Impl.Settings;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// An update handler that only ever acts on updates from the configured owner.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <remarks>
/// The whitelist check lives here, once, so a new handler cannot forget it the way a private
/// method on a growing class can be forgotten — the same guarantee <see cref="ScheduledJobBase"/>
/// gives the re-entrancy guard.
/// </remarks>
internal abstract class OwnerOnlyUpdateHandler(TelegramSettings settings) : ITelegramUpdateHandler
{
    /// <inheritdoc/>
    public abstract UpdateType Handles { get; }

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (ChatIdOf(update) != settings.OwnerChatId)
        {
            return;
        }

        await HandleOwnerUpdateAsync(update, ct);
    }

    /// <summary>
    /// Reads the chat an update belongs to.
    /// </summary>
    /// <param name="update">The update to inspect.</param>
    /// <returns>
    /// The chat id, or <see langword="null"/> if this update's shape does not carry one this
    /// handler can read. Returning <see langword="null"/> makes an unreadable shape fail closed:
    /// null never equals <see cref="TelegramSettings.OwnerChatId"/>, so the update is dropped
    /// rather than processed as if it came from the owner.
    /// </returns>
    protected abstract long? ChatIdOf(Update update);

    /// <summary>
    /// Processes an update already confirmed to be from the owner.
    /// </summary>
    /// <param name="update">The owner's update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when handling finishes.</returns>
    protected abstract Task HandleOwnerUpdateAsync(Update update, CancellationToken ct);
}
