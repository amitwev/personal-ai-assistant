using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Delivers messages through the Telegram Bot API.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="settings">Validated Telegram configuration.</param>
/// <param name="logger">
/// Where a failed best-effort delete of a task's previous announcing message is recorded.
/// </param>
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
internal sealed class TelegramNotifier(
    ITelegramBotClient bot, TelegramSettings settings, ILogger<TelegramNotifier> logger) : INotifier
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
    /// Attaches <see cref="TaskKeyboard.Actions"/>, built by <see cref="BuildKeyboard"/> the same
    /// way every other keyboard this adapter sends is. The send runs first so a delete failure
    /// can never cost the owner a reminder; the delete that follows is therefore best-effort,
    /// logged at warning and never surfaced.
    /// </remarks>
    public async Task<int> AnnounceTaskAsync(
        int? previousMessageId, Guid taskId, string text, CancellationToken ct)
    {
        var message = await bot.SendMessage(
            settings.OwnerChatId, Escape(text), ParseMode.Html,
            replyMarkup: BuildKeyboard(taskId, TaskKeyboard.Actions), cancellationToken: ct);

        if (previousMessageId is { } previous)
        {
            try
            {
                await bot.DeleteMessage(settings.OwnerChatId, previous, ct);
            }
            catch (RequestException)
            {
                logger.LogWarning(
                    "Could not delete the previous message {MessageId} announcing task {TaskId}.",
                    previous, taskId);
            }
        }

        return message.Id;
    }

    // Building three private methods rather than one -- BuildKeyboard dispatches by TaskKeyboard,
    // BuildActionsKeyboard and BuildScheduleMenuKeyboard each build one row by hand -- is
    // deliberate at this slice's size: both rows are fixed at exactly two buttons today (Done and
    // Schedule; +1h and Back), so a loop over a catalogue would be machinery for a plurality
    // that does not exist yet. Once a second preset joins the schedule menu, BuildScheduleMenuKeyboard
    // is the one method that needs to change, to iterate its own preset catalogue instead.
    private static InlineKeyboardMarkup BuildKeyboard(Guid taskId, TaskKeyboard keyboard) => keyboard switch
    {
        TaskKeyboard.Actions => BuildActionsKeyboard(taskId),
        TaskKeyboard.ScheduleMenu => BuildScheduleMenuKeyboard(taskId),
        _ => throw new ArgumentOutOfRangeException(nameof(keyboard), keyboard, "Unrecognised TaskKeyboard value."),
    };

    private static InlineKeyboardMarkup BuildActionsKeyboard(Guid taskId) => new(
        new[]
        {
            InlineKeyboardButton.WithCallbackData(
                TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, taskId)),
            InlineKeyboardButton.WithCallbackData(
                TaskNavigations.Schedule.Label,
                CallbackCodec.Encode(TaskNavigations.Schedule.Key, taskId)),
        });

    // The button's label and its wire argument are both TaskActions.PlusOneHour, not
    // TaskActions.Reschedule.Label -- that catalogue entry's label reads "Reschedule" for a
    // developer skimming the catalogue, but the button itself has always shown "+1h", and
    // PlusOneHour is the const that names what a tap on it actually sends.
    private static InlineKeyboardMarkup BuildScheduleMenuKeyboard(Guid taskId) => new(
        new[]
        {
            InlineKeyboardButton.WithCallbackData(
                TaskActions.PlusOneHour,
                CallbackCodec.Encode(TaskActions.Reschedule.Key, taskId, TaskActions.PlusOneHour)),
            InlineKeyboardButton.WithCallbackData(
                TaskNavigations.Back.Label, CallbackCodec.Encode(TaskNavigations.Back.Key, taskId)),
        });

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

    /// <inheritdoc/>
    /// <remarks>
    /// Re-attaches the same two-button keyboard <see cref="AnnounceTaskAsync"/> would build fresh for
    /// <paramref name="taskId"/> -- the task this message announces is not finished, so whatever
    /// it could already accept a tap on, it must still accept a tap on.
    /// </remarks>
    public async Task UpdateTaskAsync(int messageId, Guid taskId, string text, CancellationToken ct) =>
        await bot.EditMessageText(
            settings.OwnerChatId, messageId, Escape(text), ParseMode.Html,
            BuildKeyboard(taskId, TaskKeyboard.Actions), cancellationToken: ct);

    /// <inheritdoc/>
    /// <remarks>
    /// Builds whichever keyboard <paramref name="keyboard"/> names via the same
    /// <see cref="BuildKeyboard"/> helper every other method on this class reaches through, so a
    /// menu tap and a task edit can never disagree about what either keyboard actually contains.
    /// </remarks>
    public async Task ShowKeyboardAsync(int messageId, Guid taskId, string text, TaskKeyboard keyboard, CancellationToken ct) =>
        await bot.EditMessageText(
            settings.OwnerChatId, messageId, Escape(text), ParseMode.Html,
            BuildKeyboard(taskId, keyboard), cancellationToken: ct);

    // "&" must be replaced first. Doing "<" or ">" first and "&" after would re-escape the
    // ampersand that replacement just introduced — "<" becomes "&lt;", then that "&" becomes
    // "&amp;lt;", which renders as the literal text "&lt;" instead of "<".
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}