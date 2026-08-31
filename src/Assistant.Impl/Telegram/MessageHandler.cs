using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Sends the owner's plain-text message to the chat model and replies with its answer.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where the reply is delivered.</param>
/// <param name="scopeFactory">
/// Opens the scope <see cref="IAiClient"/> is resolved from, because this handler is a
/// singleton and a Refit client is a typed <see cref="System.Net.Http.HttpClient"/> --
/// capturing one directly would pin its message handler and defeat the factory's handler
/// rotation. <see cref="Assistant.Impl.Services.Jobs.DueReminderJob"/> already solves the
/// identical problem for <see cref="ITaskService"/>, in its own words: "Opens the scope
/// [the service] is resolved from, because this job is a singleton and the service depends on
/// the scoped database context."
/// </param>
/// <remarks>
/// The owner check lives inline here, on purpose: the assistant serves exactly one person, so
/// there is nothing to route between. Any future handler must apply the same check itself --
/// nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/> enforces it.
/// </remarks>
internal sealed class MessageHandler(
    TelegramSettings settings, INotifier notifier, IServiceScopeFactory scopeFactory)
    : ITelegramUpdateHandler
{
    private const string Unreachable =
        "I could not reach the model just now. Send that again in a moment.";

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

        using var scope = scopeFactory.CreateScope();
        var ai = scope.ServiceProvider.GetRequiredService<IAiClient>();
        var answer = await ai.AskAsync(text, ct);

        await notifier.SendAsync(answer.IsSuccess ? answer.Value! : Unreachable, ct);
    }
}
