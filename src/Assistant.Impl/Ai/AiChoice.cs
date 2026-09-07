namespace Assistant.Impl.Ai;

/// <summary>
/// One candidate answer within a response from the chat API.
/// </summary>
/// <param name="Message">The answer itself, in the same shape a request message takes.</param>
/// <param name="FinishReason">
/// The provider's own field describing why it stopped generating. Nullable because a
/// provider is not obliged to send one.
/// </param>
internal sealed record AiChoice(AiMessage Message, string? FinishReason);
