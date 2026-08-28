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
/// <para>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself —
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// </para>
/// </remarks>
internal sealed class MessageHandler(TelegramSettings settings, INotifier notifier)
    : ITelegramUpdateHandler
{
    /// <inheritdoc/>
    public UpdateType Handles => UpdateType.Message;

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.Message is not { Chat.Id: var chatId, Text: { } text } ||
            chatId != settings.OwnerChatId)
        {
            return;
        }

        await notifier.SendAsync(text, ct);
    }
}
