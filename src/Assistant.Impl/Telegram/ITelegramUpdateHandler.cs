using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Handles one kind of inbound Telegram update.
/// </summary>
/// <remarks>
/// This lives in <c>Assistant.Impl</c>, not <c>Assistant.Interfaces</c>, even though every other
/// interface in the project lives there: this one names <see cref="Update"/>, and
/// <c>DependencyRuleTests.Interfaces_do_not_depend_on_infrastructure_libraries</c> fails the build
/// if <c>Assistant.Interfaces</c> references <c>Telegram.Bot</c>. Do not move it there.
/// </remarks>
internal interface ITelegramUpdateHandler
{
    /// <summary>
    /// The kind of update this handler processes.
    /// </summary>
    /// <value>
    /// Used by <see cref="TelegramListener"/> both to build its <c>allowedUpdates</c> filter and
    /// to route each fetched update to the handlers that claim it.
    /// </value>
    UpdateType Handles { get; }

    /// <summary>
    /// Processes one update.
    /// </summary>
    /// <param name="update">The update to handle. Its <see cref="Update.Type"/> matches <see cref="Handles"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when handling finishes.</returns>
    Task HandleAsync(Update update, CancellationToken ct);
}
