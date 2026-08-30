using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Reaches a chat model with the owner's text and returns its answer.
/// </summary>
/// <remarks>
/// A transport abstraction, not one of spec §3.6's behaviour seams: this interface changes
/// shape at F9b, when <c>AskAsync</c> starts returning <c>Result&lt;ToolCall&gt;</c> so a
/// tool invocation can be parsed out of the answer. F9b's growing seam is
/// <c>IAssistantTool</c>, not this one.
/// </remarks>
public interface IAiClient
{
    /// <summary>
    /// Sends the owner's text to the configured model and returns its answer.
    /// </summary>
    /// <param name="userText">What the owner said.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The model's answer, or the reason it could not be reached.
    /// </returns>
    Task<Result<string>> AskAsync(string userText, CancellationToken ct);
}
