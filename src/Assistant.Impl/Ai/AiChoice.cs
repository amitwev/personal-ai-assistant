namespace Assistant.Impl.Ai;

/// <summary>
/// One candidate answer within a response from the chat API.
/// </summary>
/// <param name="Message">The answer itself, in the same shape a request message takes.</param>
internal sealed record AiChoice(AiMessage Message);
