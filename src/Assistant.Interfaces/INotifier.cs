namespace Assistant.Interfaces;

/// <summary>
/// Delivers a message to the person the assistant works for.
/// </summary>
/// <remarks>
/// The recipient is configuration, not a parameter: this is a single-user assistant, so every
/// call site would otherwise pass the same value. Rendering is the caller's job — a notifier
/// delivers text it is given and never sees a database shape.
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
