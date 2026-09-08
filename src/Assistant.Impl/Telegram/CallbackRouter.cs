using Assistant.Contracts;
using Assistant.Impl.Mapping;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Assistant.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Routes an inline button's tap to the <see cref="ITaskAction"/> its callback data names, or
/// swaps the message's keyboard when the callback names an <see cref="ITaskNavigation"/>, then
/// always answers the callback query.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="notifier">Where a successful action's edit is delivered.</param>
/// <param name="actions">
/// Every registered task action, resolved by matching <see cref="TaskActionDefinition.Key"/>
/// against each one's <see cref="ITaskAction.Definition"/>.
/// </param>
/// <param name="navigations">
/// Every registered keyboard navigation, resolved the same way <paramref name="actions"/> is, by
/// matching <see cref="TaskNavigationDefinition.Key"/> against each one's
/// <see cref="ITaskNavigation.Definition"/>. Tried only once no registered action's key matches,
/// since the two catalogues' keys are disjoint by construction (<c>done</c>/<c>schedule</c> versus
/// <c>menu</c>/<c>back</c>) and never need to race.
/// </param>
/// <param name="clock">Renders a stored due instant back in the configured local zone.</param>
/// <remarks>
/// The callback query is answered last in every branch, after any edit a successful action
/// triggers, never before. The sole exception is the first guard's bare early return, unreachable
/// in practice since <see cref="TelegramListener.DispatchAsync"/> only invokes handlers whose
/// <see cref="Handles"/> matches the update's own type.
/// <para>
/// Which edit a successful action gets is decided by the resulting task's own
/// <see cref="ReminderTask.Status"/>, never by which action ran: a
/// <see cref="ReminderStatus.Completed"/> task strikes through and loses its keyboard via
/// <see cref="INotifier.MarkCompletedTaskAsync"/>; any other status re-renders in place via
/// <see cref="INotifier.UpdateTaskAsync"/>, text rebuilt fresh from the task. This method names
/// neither <c>DoneAction</c> nor <c>ScheduleAction</c> anywhere in its body -- the task's own
/// status decides, so a future action needs no change here. A successful action edit also
/// attaches the main actions keyboard, closing any menu the tap came from -- no action needs to
/// know a menu was open to close it.
/// </para>
/// <para>
/// <c>Message.Text</c> is bound with a plain <c>var</c> because Telegram omits it once a message
/// is judged too old to still carry content. This guards <see cref="INotifier.MarkCompletedTaskAsync"/>
/// and <see cref="INotifier.ShowKeyboardAsync"/>, which each need prior text (to strike through or
/// preserve, respectively); <see cref="INotifier.UpdateTaskAsync"/> builds its text fresh from the
/// task and runs unconditionally.
/// </para>
/// <para>
/// The owner check lives inline here, the same as <see cref="MessageHandler"/>'s own remarks
/// explain. Unlike <see cref="MessageHandler"/>, a non-owner's tap is still answered, per spec 6.4,
/// but the action itself never runs and nothing is edited.
/// </para>
/// </remarks>
internal sealed class CallbackRouter(
    TelegramSettings settings,
    ITelegramBotClient bot,
    INotifier notifier,
    IEnumerable<ITaskAction> actions,
    IEnumerable<ITaskNavigation> navigations,
    ILocalTimeResolver clock) : ITelegramUpdateHandler
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

        if (!CallbackCodec.TryDecode(data, out var actionKey, out var taskId, out var argument))
        {
            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var action = actions.FirstOrDefault(a => a.Definition.Key == actionKey);

        if (action is null)
        {
            var navigation = navigations.FirstOrDefault(n => n.Definition.Key == actionKey);

            if (navigation is not null)
            {
                if (messageText is not null)
                {
                    await notifier.ShowKeyboardAsync(messageId, taskId, messageText, navigation.Definition.Shows, ct);
                }

                await bot.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct);
                return;
            }

            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var result = await action.ExecuteAsync(taskId, argument, ct);

        if (result.IsSuccess)
        {
            var task = result.Value!;

            if (task.Status == ReminderStatus.Completed)
            {
                if (messageText is not null)
                {
                    await notifier.MarkCompletedTaskAsync(messageId, messageText, ct);
                }
            }
            else
            {
                await notifier.UpdateTaskAsync(messageId, task.Id, task.ToMessageText(clock), ct);
            }
        }

        var reply = result switch
        {
            { IsSuccess: true } => null,
            { Error: ErrorCode.TaskAlreadyCompleted } => AlreadyDone,
            { Error: ErrorCode.TaskActionArgumentUnrecognized } => ThatButtonIsNoLongerValid,
            _ => CouldNotFindThatTask,
        };

        await bot.AnswerCallbackQuery(callbackQueryId, reply, cancellationToken: ct);
    }
}
