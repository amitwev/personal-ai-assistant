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
/// <para>
/// Escaping happens here, not at call sites, because this is the only type that knows it is
/// sending HTML. Every caller today sends plain text and nothing sends markup, so a
/// text-versus-markup distinction would be an abstraction with a single case — the project's
/// YAGNI rule forbids that. F10 is the first feature that renders markup; it introduces the
/// distinction then, with a test that demands it.
/// </para>
/// </remarks>
internal sealed class TelegramNotifier(ITelegramBotClient bot, TelegramSettings settings) : INotifier
{
    /// <inheritdoc/>
    public async Task SendAsync(string text, CancellationToken ct) =>
        await bot.SendMessage(settings.OwnerChatId, Escape(text), ParseMode.Html, cancellationToken: ct);

    // "&" must be replaced first. Doing "<" or ">" first and "&" after would re-escape the
    // ampersand that replacement just introduced — "<" becomes "&lt;", then that "&" becomes
    // "&amp;lt;", which renders as the literal text "&lt;" instead of "<".
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
