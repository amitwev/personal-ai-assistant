using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Runs the language model tool loop for one user message.
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Interprets a message, invoking tools as the model requests them.
    /// </summary>
    /// <param name="userText">The user's message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The reply to show the user, and the identifier of a task the turn created, if any, so the
    /// caller can attach action buttons. A failure carrying
    /// <see cref="ErrorCode.LlmUnavailable"/> when every provider failed.
    /// </returns>
    Task<(Result Result, string ReplyText, Guid? CreatedTaskId)> RunAsync(string userText, CancellationToken ct);
}
