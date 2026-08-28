using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Polls Telegram for inbound updates and dispatches each one to the handlers that claim it.
/// </summary>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="handlers">Every registered update handler. Each declares the update kind it handles.</param>
/// <param name="timeProvider">Supplies the delay applied after a failed poll.</param>
/// <param name="logger">Where a failure is recorded.</param>
/// <remarks>
/// The offset is advanced before an update is dispatched, not after. Dispatching first would
/// re-poll an update whose handler always throws, forever and at full speed, so one
/// malformed message would wedge the assistant and hammer Telegram. Advancing first
/// costs at most one dropped reply instead. This is the opposite of the reminder path's
/// send-then-mark ordering, because there a lost message is the product's core failure
/// while here it costs the owner one retype.
/// <para>
/// An update no handler claims is ignored silently rather than logged as unexpected: Telegram's
/// documentation states that <c>allowedUpdates</c> does not affect updates already queued before
/// the call that set it, so a kind this listener did not ask for can still arrive.
/// </para>
/// </remarks>
internal sealed class TelegramListener(
    ITelegramBotClient bot,
    IEnumerable<ITelegramUpdateHandler> handlers,
    TimeProvider timeProvider,
    ILogger<TelegramListener> logger) : BackgroundService
{
    private const int LongPollSeconds = 30;

    private static readonly TimeSpan PollFailureBackoff = TimeSpan.FromSeconds(5);

    private readonly UpdateType[] _allowedUpdates = handlers.Select(h => h.Handles).Distinct().ToArray();

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
        var updates = await FetchAsync(ct);

        foreach (var update in updates)
        {
            _offset = update.Id + 1;

            await DispatchAsync(update, ct);
        }
    }

    private async Task<Update[]> FetchAsync(CancellationToken ct)
    {
        try
        {
            return await bot.GetUpdates(
                offset: _offset,
                limit: null,
                timeout: LongPollSeconds,
                allowedUpdates: _allowedUpdates,
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Polling Telegram for updates failed; the loop continues.");
            await Task.Delay(PollFailureBackoff, timeProvider, ct);
            return [];
        }
    }

    private async Task DispatchAsync(Update update, CancellationToken ct)
    {
        foreach (var handler in handlers.Where(h => h.Handles == update.Type))
        {
            try
            {
                await handler.HandleAsync(update, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex, "Handling update {UpdateId} failed; the loop continues.", update.Id);
            }
        }
    }
}
