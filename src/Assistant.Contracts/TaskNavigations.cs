namespace Assistant.Contracts;

/// <summary>
/// Every navigation an inline button can perform, in the one place both <c>CallbackRouter</c> and
/// <c>TelegramNotifier</c> can read -- the seam <see cref="TaskActions"/> is for a tap that acts
/// on a task, mirrored here for a tap that only changes which keyboard is showing.
/// </summary>
/// <remarks>
/// <see cref="Schedule"/> is declared before <see cref="All"/> because C# runs a type's static
/// member initializers in declaration order, and <see cref="All"/>'s own initializer reads
/// <see cref="Schedule"/> and <see cref="Back"/> -- reversing the order compiles cleanly but
/// leaves <see cref="All"/> holding a <see langword="null"/> element, since the one declared later
/// would not yet have run.
/// </remarks>
public static class TaskNavigations
{
    /// <summary>
    /// The main keyboard's button that opens the schedule menu.
    /// </summary>
    public static TaskNavigationDefinition Schedule { get; } = new(
        Key: "schedule",
        Label: "Schedule",
        Shows: TaskKeyboard.ScheduleMenu,
        Description: "Opens the schedule menu. Changes no task field -- only the keyboard "
            + "attached to the message changes.");

    /// <summary>
    /// The schedule menu's button that closes it again without applying a preset.
    /// </summary>
    public static TaskNavigationDefinition Back { get; } = new(
        Key: "back",
        Label: "Back",
        Shows: TaskKeyboard.Actions,
        Description: "Closes the schedule menu, returning to the main keyboard. Changes no task "
            + "field -- only the keyboard attached to the message changes.");

    /// <summary>
    /// Every declared navigation, in declaration order.
    /// </summary>
    public static IReadOnlyList<TaskNavigationDefinition> All { get; } = [Schedule, Back];
}
