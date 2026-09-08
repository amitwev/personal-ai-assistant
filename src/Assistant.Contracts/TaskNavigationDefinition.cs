namespace Assistant.Contracts;

/// <summary>
/// One tap that swaps which keyboard is attached to a task message, without acting on the task
/// itself -- declared once so every consumer -- the router that resolves it, and the notifier
/// that renders it -- shares the same definition.
/// </summary>
/// <param name="Key">
/// The navigation's key, as carried on the wire inside the callback codec. Must never contain a
/// colon -- <c>CallbackCodec.TryDecode</c> splits its input on <c>:</c>, so a key that contained
/// one would render a button that is undecodable forever once tapped.
/// </param>
/// <param name="Label">
/// The text a human reads on the button itself.
/// </param>
/// <param name="Shows">
/// Which keyboard a tap on this button attaches in place of whichever one the message already
/// carried.
/// </param>
/// <param name="Description">
/// What the navigation does, written for a developer reading this catalogue.
/// </param>
/// <remarks>
/// <see cref="Description"/> has no runtime consumer, the same as
/// <see cref="TaskActionDefinition.Description"/> -- see that type's own remarks for why it stays.
/// </remarks>
public sealed record TaskNavigationDefinition(string Key, string Label, TaskKeyboard Shows, string Description);
