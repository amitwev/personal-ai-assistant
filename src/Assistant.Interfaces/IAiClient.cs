using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Reaches a chat model with the owner's text and returns the tool call it chose.
/// </summary>
/// <remarks>
/// A transport abstraction, not one of spec §3.6's behaviour seams: it has exactly one
/// production implementation, <c>AiClient</c>, and still does after this slice changes
/// <c>AskAsync</c>'s return type — a modification, not an extension. The seam this slice grows
/// is <see cref="IAssistantTool"/>, not this interface.
/// </remarks>
public interface IAiClient
{
    /// <summary>
    /// Sends the owner's text, the system prompt, and every registered tool definition to the
    /// configured model, and returns the tool call it chose.
    /// </summary>
    /// <param name="userText">What the owner said.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The tool call the model asked for, or the reason none came back.
    /// </returns>
    Task<Result<ToolCall>> AskAsync(string userText, CancellationToken ct);
}
