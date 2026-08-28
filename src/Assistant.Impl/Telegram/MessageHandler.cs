using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Echoes a plain-text message from the owner back through the notifier.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <remarks>
/// No AI yet: this is F7's placeholder response, replaced once a real reply is composed.
/// </remarks>
internal sealed class MessageHandler(TelegramSettings settings, INotifier notifier)
    : OwnerOnlyUpdateHandler(settings)
{
    /// <inheritdoc/>
    public override UpdateType Handles => UpdateType.Message;

    /// <inheritdoc/>
    protected override long? ChatIdOf(Update update) => update.Message?.Chat.Id;

    /// <inheritdoc/>
    protected override async Task HandleOwnerUpdateAsync(Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } text)
        {
            return;
        }

        await notifier.SendAsync(text, ct);
    }
}
