using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model and replies once it names a tool.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="ai">Reaches the configured chat model for an answer.</param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself --
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// This handler is registered scoped and resolved fresh, inside a scope
/// <see cref="TelegramListener.DispatchAsync"/> opens per update, so <see cref="IAiClient"/> is
/// injected directly -- there is no captive-dependency concern the way there was when this
/// handler was a singleton.
/// </remarks>
internal sealed class MessageHandler(TelegramSettings settings, INotifier notifier, IAiClient ai)
    : ITelegramUpdateHandler
{
    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

    private const string ToolCallNotActedOnYet =
        "Got it -- I understood that as a task, but I cannot save it yet.";

    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";

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

        var result = await ai.AskAsync(text, ct);

        var reply = result switch
        {
            { IsSuccess: true } => ToolCallNotActedOnYet,
            { Error: ErrorCode.ModelReturnedNoToolCall } => NotUnderstoodAsATask,
            _ => Unreachable,
        };

        await notifier.SendAsync(reply, ct);
    }
}
