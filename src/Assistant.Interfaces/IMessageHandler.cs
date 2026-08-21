namespace Assistant.Interfaces;

/// <summary>
/// Handles one inbound text message from the user.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Processes a message and replies to it.
    /// </summary>
    /// <param name="senderUserId">The messaging platform's identifier for the sender.</param>
    /// <param name="text">The message body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes once the message has been handled and any reply sent. Messages from
    /// anyone other than the configured owner are discarded without a reply and without any
    /// language model call.
    /// </returns>
    Task HandleAsync(long senderUserId, string text, CancellationToken ct);
}
