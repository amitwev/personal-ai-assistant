namespace Assistant.Contracts;

/// <summary>
/// Which inline keyboard is attached to a task message.
/// </summary>
/// <remarks>
/// A task message carries exactly one keyboard at a time, and every keyboard shows the same
/// task -- swapping which one is attached, in response to a navigation tap, changes no task field
/// at all. See <see cref="TaskNavigationDefinition.Shows"/>.
/// </remarks>
public enum TaskKeyboard
{
    /// <summary>
    /// Unset default. Never valid to render.
    /// </summary>
    Unknown,

    /// <summary>
    /// The main keyboard every task message starts with: Done, and the button that opens the
    /// schedule menu.
    /// </summary>
    Actions,

    /// <summary>
    /// The schedule menu: one button per recognised preset, and Back.
    /// </summary>
    ScheduleMenu,
}
