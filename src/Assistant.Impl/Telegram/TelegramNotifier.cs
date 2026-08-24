using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Delivers messages through the Telegram Bot API.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="settings">Validated Telegram configuration.</param>
/// <remarks>
/// HTML parse mode is deliberate. MarkdownV2 has eighteen escape-sensitive characters, so an
/// underscore in a task title would produce a 400 on a live reminder — a formatting defect that
/// costs a delivery. HTML has three, and none occur in ordinary task text.
/// </remarks>
internal sealed class TelegramNotifier(ITelegramBotClient bot, TelegramSettings settings) : INotifier
{
    /// <inheritdoc/>
    public async Task SendAsync(string text, CancellationToken ct) =>
        await bot.SendMessage(settings.OwnerChatId, text, ParseMode.Html, cancellationToken: ct);
}
