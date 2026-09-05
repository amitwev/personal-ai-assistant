using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

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
    // new InlineKeyboardMarkup([]) is the wrong empty keyboard: an empty array of buttons binds
    // to the constructor overload that wraps it in one row, producing {"inline_keyboard":[[]]}
    // on the wire -- one empty row, not an empty keyboard. Only the parameterless constructor
    // produces {"inline_keyboard":[]}, the shape Telegram treats as "no keyboard."
    private static readonly InlineKeyboardMarkup NoButtons = new();

    /// <inheritdoc/>
    public async Task SendAsync(string text, CancellationToken ct) =>
        await bot.SendMessage(settings.OwnerChatId, Escape(text), ParseMode.Html, cancellationToken: ct);

    /// <inheritdoc/>
    /// <remarks>
    /// Renders completion by wrapping the escaped text in an inline &lt;s&gt; element -- this
    /// adapter's own choice of how to show completion, not part of the interface's contract. The
    /// edit also sends <see cref="NoButtons"/>, an explicit empty keyboard, so a completed
    /// reminder does not keep a dead Done button visible under its struck-through title.
    /// </remarks>
    public async Task MarkCompletedTaskAsync(int messageId, string text, CancellationToken ct) =>
        await bot.EditMessageText(
            settings.OwnerChatId, messageId, $"<s>{Escape(text)}</s>", ParseMode.Html, NoButtons,
            cancellationToken: ct);

    // "&" must be replaced first. Doing "<" or ">" first and "&" after would re-escape the
    // ampersand that replacement just introduced — "<" becomes "&lt;", then that "&" becomes
    // "&amp;lt;", which renders as the literal text "&lt;" instead of "<".
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
