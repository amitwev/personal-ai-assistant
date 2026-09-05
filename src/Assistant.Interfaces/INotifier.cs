namespace Assistant.Interfaces;

/// <summary>
/// Delivers a message to the person the assistant works for.
/// </summary>
/// <remarks>
/// The recipient is configuration, not a parameter: this is a single-user assistant, so every
/// call site would otherwise pass the same value. Rendering the message body is the caller's
/// job -- a notifier escapes and formats text it is given, never composing prose of its own. A
/// task identifier is different: it is the channel-neutral handle an adapter needs to build
/// whatever affordance its own channel supports (a callback button, a deep link, ...), not a
/// database shape. A caller passing a pre-built, channel-specific token instead -- Telegram's
/// own <c>v1:done:...</c> callback string, say -- would leak one channel's wire format into an
/// interface every future channel must also implement.
/// </remarks>
public interface INotifier
{
    /// <summary>
    /// Sends a message to the owner.
    /// </summary>
    /// <param name="text">
    /// The message body, as plain text. The adapter escapes whatever its channel requires
    /// before sending, so callers must not pre-escape.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendAsync(string text, CancellationToken ct);

    /// <summary>
    /// Sends a message announcing a task, with every action from the shared catalogue attached
    /// as a button.
    /// </summary>
    /// <param name="taskId">
    /// The task the message announces. The adapter needs this to build a channel-neutral handle
    /// for every action in the catalogue -- it never sees any other part of a database shape.
    /// </param>
    /// <param name="text">
    /// The message body, as plain text. The adapter escapes whatever its channel requires
    /// before sending, so callers must not pre-escape.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    /// <remarks>
    /// Every action in <c>TaskActions.All</c> is attached; there is no overload that accepts a
    /// subset, because every caller today wants the same one -- the day a caller needs fewer,
    /// that caller is the trigger for adding one.
    /// </remarks>
    Task SendTaskAsync(Guid taskId, string text, CancellationToken ct);

    /// <summary>
    /// Updates a previously sent message to reflect that the task it announced is now complete.
    /// </summary>
    /// <param name="messageId">Identifier of the message to edit.</param>
    /// <param name="text">
    /// The plain, unescaped text the message originally carried. The adapter escapes it and
    /// applies its own rendering for completion; callers must not pre-format or pre-escape.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the edit has been accepted.</returns>
    /// <remarks>
    /// Sends an explicit empty keyboard, clearing whatever inline keyboard the message already
    /// carries -- see <c>TelegramNotifier.MarkCompletedTaskAsync</c> for why an empty keyboard is
    /// not simply omitting the argument.
    /// </remarks>
    Task MarkCompletedTaskAsync(int messageId, string text, CancellationToken ct);
}
