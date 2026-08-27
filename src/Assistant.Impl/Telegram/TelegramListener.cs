using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Polls Telegram for inbound updates and answers the ones the owner sent.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="notifier">Where a reply is delivered.</param>
/// <param name="timeProvider">Supplies the delay applied after a failed poll.</param>
/// <param name="logger">Where a failure is recorded.</param>
/// <remarks>
/// The offset is advanced before an update is handled, not after. Handling first would
/// re-poll an update whose handler always throws, forever and at full speed, so one
/// malformed message would wedge the assistant and hammer Telegram. Advancing first
/// costs at most one dropped reply instead. This is the opposite of the reminder path's
/// send-then-mark ordering, because there a lost message is the product's core failure
/// while here it costs the owner one retype.
/// </remarks>
internal sealed class TelegramListener(
    ITelegramBotClient bot,
    TelegramSettings settings,
    INotifier notifier,
    TimeProvider timeProvider,
    ILogger<TelegramListener> logger) : BackgroundService
{
    private const int LongPollSeconds = 30;

    private static readonly TimeSpan PollFailureBackoff = TimeSpan.FromSeconds(5);

    private int? _offset;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure.
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        Update[] updates;

        try
        {
            updates = await bot.GetUpdates(
                offset: _offset,
                limit: null,
                timeout: LongPollSeconds,

                // F6 must add UpdateType.CallbackQuery here, or its buttons never fire.
                allowedUpdates: [UpdateType.Message],
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Polling Telegram for updates failed; the loop continues.");
            await Task.Delay(PollFailureBackoff, timeProvider, ct);
            return;
        }

        foreach (var update in updates)
        {
            _offset = update.Id + 1;

            try
            {
                await HandleAsync(update, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex, "Handling update {UpdateId} failed; the loop continues.", update.Id);
            }
        }
    }

    private async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.Message is not { Text: { } text } message
            || message.Chat.Id != settings.OwnerChatId)
        {
            return;
        }

        await notifier.SendAsync(text, ct);
    }
}
