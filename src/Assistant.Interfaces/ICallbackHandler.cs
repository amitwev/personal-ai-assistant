namespace Assistant.Interfaces;

/// <summary>
/// Handles one inbound button press.
/// </summary>
public interface ICallbackHandler
{
    /// <summary>
    /// Processes a button press and updates the originating message.
    /// </summary>
    /// <param name="senderUserId">The messaging platform's identifier for the sender.</param>
    /// <param name="callbackId">Identifier the platform requires in order to acknowledge the press.</param>
    /// <param name="messageId">Identifier of the message the button belongs to.</param>
    /// <param name="callbackData">The button's payload, in the form <c>v1:action:id[:arg]</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes once the press has been acknowledged. The press is always
    /// acknowledged, including on failure, because an unacknowledged press leaves the user's
    /// client showing a spinner indefinitely.
    /// </returns>
    Task HandleAsync(long senderUserId, string callbackId, int messageId, string callbackData, CancellationToken ct);
}
