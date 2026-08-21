namespace Assistant.Models;

/// <summary>
/// One turn of the conversation, retained so follow-up messages resolve.
/// </summary>
/// <remarks>
/// Only the most recent turns are ever read; see the chat message repository for the window size.
/// </remarks>
public sealed class ChatMessage
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Who produced this turn: <c>user</c>, <c>assistant</c>, or <c>tool</c>.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The turn's text.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// When the turn occurred, in UTC.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
