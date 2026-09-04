using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Routes an inline button's tap to the <see cref="ITaskAction"/> its callback data names, then
/// always answers the callback query.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="notifier">Where the completed-task edit is delivered on a successful action.</param>
/// <param name="actions">
/// Every registered task action, resolved by matching <see cref="TaskActionDefinition.Key"/>
/// against each one's <see cref="ITaskAction.Definition"/>.
/// </param>
/// <remarks>
/// The callback query is answered last in every branch, after any edit a successful action
/// triggers, never before -- every reachable path through <see cref="HandleAsync"/> ends with
/// exactly one call to <see cref="ITelegramBotClient"/>'s answer method and nothing after it, so
/// observing that one call is enough to know the whole update has been fully handled. The sole
/// exception is the first guard's bare early return, which answers nothing because there is no
/// callback query to answer at all -- and that branch is unreachable in practice, since
/// <see cref="TelegramListener.DispatchAsync"/> only invokes handlers whose <see cref="Handles"/>
/// matches the update's own type, and this handler declares <see cref="UpdateType.CallbackQuery"/>.
/// <para>
/// <c>Message.Text</c> is bound with a plain <c>var</c>, not a null-checked pattern, because
/// Telegram omits a message's text once it judges the message too old to still carry content --
/// exactly the age an old reminder's Done button can reach in chat history. The action still
/// runs and the query is still answered in that case; only the completed-task edit is skipped,
/// since there is no text left to strike through.
/// </para>
/// <para>
/// The owner check lives inline here, the same as <see cref="MessageHandler"/>'s own remarks
/// explain: nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/>
/// enforces it. Unlike <see cref="MessageHandler"/>, a non-owner's tap is still answered -- spec
/// 6.4 requires every callback query to be answered, owner or not, or Telegram leaves that
/// tapper's own client spinning -- but the action itself never runs and nothing is edited.
/// </para>
/// </remarks>
internal sealed class CallbackRouter(
    TelegramSettings settings,
    ITelegramBotClient bot,
    INotifier notifier,
    IEnumerable<ITaskAction> actions) : ITelegramUpdateHandler
{
    private const string ThatButtonIsNoLongerValid = "That button is no longer valid.";

    private const string AlreadyDone = "Already done.";

    private const string CouldNotFindThatTask = "I could not find that task.";

    /// <inheritdoc/>
    public UpdateType Handles => UpdateType.CallbackQuery;

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is not { } callbackQuery)
        {
            return;
        }

        if (callbackQuery is not
            {
                Id: var callbackQueryId,
                Data: { } data,
                Message: { Chat.Id: var chatId, Id: var messageId, Text: var messageText },
            })
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        if (chatId != settings.OwnerChatId)
        {
            await bot.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct);
            return;
        }

        if (!CallbackCodec.TryDecode(data, out var actionKey, out var taskId))
        {
            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var action = actions.FirstOrDefault(a => a.Definition.Key == actionKey);

        if (action is null)
        {
            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var result = await action.ExecuteAsync(taskId, ct);

        if (result.IsSuccess && messageText is not null)
        {
            await notifier.MarkCompletedTaskAsync(messageId, messageText, ct);
        }

        var reply = result switch
        {
            { IsSuccess: true } => null,
            { Error: ErrorCode.TaskAlreadyCompleted } => AlreadyDone,
            _ => CouldNotFindThatTask,
        };

        await bot.AnswerCallbackQuery(callbackQueryId, reply, cancellationToken: ct);
    }
}
