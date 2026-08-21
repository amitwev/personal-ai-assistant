using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>
/// Persistence for the rolling conversation window.
/// </summary>
public interface IChatMessageRepository
{
    /// <summary>
    /// Appends one turn.
    /// </summary>
    /// <param name="message">The turn to append.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task AppendAsync(ChatMessage message, CancellationToken ct);

    /// <summary>
    /// Returns the most recent turns, oldest first.
    /// </summary>
    /// <param name="limit">How many turns to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Up to <paramref name="limit"/> turns in chronological order, ready to be replayed to the
    /// language model as conversation history.
    /// </returns>
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(int limit, CancellationToken ct);
}
