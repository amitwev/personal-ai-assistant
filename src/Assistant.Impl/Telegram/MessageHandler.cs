using System.Globalization;
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model, carries out the tool call it names,
/// and replies with what was actually stored.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="ai">Reaches the configured chat model for an answer.</param>
/// <param name="tools">Every registered tool, matched against the model's tool call by name.</param>
/// <param name="clock">Renders a stored due instant back in the configured local zone.</param>
/// <param name="logger">Where a tool call naming an unregistered tool is recorded.</param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself --
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// This handler is registered scoped and resolved fresh, inside a scope
/// <see cref="TelegramListener.DispatchAsync"/> opens per update, so every dependency here is
/// injected directly -- there is no captive-dependency concern the way there was when this
/// handler was a singleton.
/// <para>
/// Tool dispatch is a plain lookup against <paramref name="tools"/>, the same shape
/// <c>CallbackRouter</c> already uses to match an inbound key against
/// <c>IEnumerable&lt;ITaskAction&gt;</c>: an inbound name matched against a registered
/// collection, extended by adding a class and a registration, never by editing this method.
/// </para>
/// </remarks>
internal sealed class MessageHandler(
    TelegramSettings settings,
    INotifier notifier,
    IAiClient ai,
    IEnumerable<IAssistantTool> tools,
    ILocalTimeResolver clock,
    ILogger<MessageHandler> logger)
    : ITelegramUpdateHandler
{
    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";

    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";

    private const string DueTimeInPastReply =
        "That time has already passed. What time did you mean?";

    private const string DueTimeTooFarAheadReply =
        "That is more than two years away, which is probably not what you meant. "
        + "What time did you mean?";

    private const string DueTimeUnparseableReply =
        "I could not make sense of that time. What time did you mean?";

    private const string TitleMissingReply =
        "I did not catch what to call that. What should I call it?";

    private const string SomethingWentWrongReply =
        "Something went wrong on my end. Send that again in a moment.";

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

        var answer = await ai.AskAsync(text, ct);

        if (!answer.IsSuccess)
        {
            var failure = answer switch
            {
                { Error: ErrorCode.ModelReturnedNoToolCall } => NotUnderstoodAsATask,
                _ => Unreachable,
            };

            await notifier.SendAsync(failure, ct);
            return;
        }

        var toolCall = answer.Value!;
        var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);

        Result<ReminderTask> outcome;

        if (tool is null)
        {
            logger.LogWarning("The chat model called an unregistered tool {Tool}.", toolCall.Name);
            outcome = Result<ReminderTask>.Failure(ErrorCode.ModelNamedUnknownTool);
        }
        else
        {
            outcome = await tool.ExecuteAsync(toolCall.ArgumentsJson, ct);
        }

        if (!outcome.IsSuccess)
        {
            var failure = outcome switch
            {
                { Error: ErrorCode.DueTimeInPast } => DueTimeInPastReply,
                { Error: ErrorCode.DueTimeTooFarAhead } => DueTimeTooFarAheadReply,
                { Error: ErrorCode.DueTimeUnparseable } => DueTimeUnparseableReply,
                { Error: ErrorCode.ToolArgumentMissing } => TitleMissingReply,
                _ => SomethingWentWrongReply,
            };

            await notifier.SendAsync(failure, ct);
            return;
        }

        var task = outcome.Value!;
        var reply = task.DueAt is { } dueAt
            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
            : $"{task.Title} -- saved with no reminder.";

        await notifier.SendTaskAsync(task.Id, reply, ct);
    }
}
