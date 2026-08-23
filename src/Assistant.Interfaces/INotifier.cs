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
    /// <param name="text">The message body, already rendered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the message has been accepted for delivery.</returns>
    Task SendAsync(string text, CancellationToken ct);
}
