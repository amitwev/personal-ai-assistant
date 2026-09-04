namespace Assistant.Contracts;

/// <summary>
/// One action an inline button can perform on a task, declared once so every consumer -- the
/// router that resolves it, and a future button that renders it -- shares the same definition.
/// </summary>
/// <param name="Key">
/// The action's key, as carried on the wire inside the callback codec. Must never contain a
/// colon -- <c>CallbackCodec.TryDecode</c> splits its input on <c>:</c>, so a key that contained
/// one would render a button that is undecodable forever once tapped.
/// </param>
/// <param name="Label">
/// The text a human reads on the button itself.
/// </param>
/// <param name="Description">
/// What the action does, written for a developer reading this catalogue.
/// </param>
/// <remarks>
/// <see cref="Description"/> has no runtime consumer. Nothing sends it anywhere -- there is no
/// <c>/help</c> command, and actions are never described to the chat model, unlike
/// <c>IAssistantTool.Description</c>, which <c>AiClient.ToWireTool</c> does send on the wire. It
/// exists solely for the person reading this catalogue: do not delete it as dead code, and do
/// not go looking for the call site that transmits it -- there is not one.
/// </remarks>
public sealed record TaskActionDefinition(string Key, string Label, string Description);
