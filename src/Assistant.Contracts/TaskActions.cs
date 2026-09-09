namespace Assistant.Contracts;

/// <summary>
/// Every action an inline button can perform on a task, in the one place both
/// <c>CallbackRouter</c> and a future button-rendering caller can read.
/// </summary>
/// <remarks>
/// <see cref="Done"/> is declared before <see cref="All"/> because C# runs a type's static
/// member initializers in declaration order, and <see cref="All"/>'s own initializer reads
/// <see cref="Done"/> -- reversing the order compiles cleanly but leaves <see cref="All"/>
/// holding a <see langword="null"/> element, since <see cref="Done"/> would not yet have run.
/// </remarks>
public static class TaskActions
{
    /// <summary>
    /// The schedule menu's one preset button today, both its own label and the argument it
    /// carries on the wire.
    /// </summary>
    /// <remarks>
    /// A single const serves both roles only because there is exactly one preset, so its button
    /// text and its wire argument happen to be the identical string. They are not the same thing
    /// on principle -- a future preset such as "Tonight 20:00" would need a label with a space and
    /// a colon next to a lowercase, colon-free argument such as <c>tonight</c>. F11-4b gives each
    /// preset its own type carrying a separate <c>Argument</c> and <c>Label</c>, which is where
    /// this const's two roles stop being the same thing and each gets its own home.
    /// </remarks>
    public const string PlusOneHour = "+1h";

    /// <summary>
    /// The Done button's definition.
    /// </summary>
    public static TaskActionDefinition Done { get; } = new(
        Key: "done",
        Label: "Done",
        Description: "Marks the task complete. Refused when the task is already complete.");

    /// <summary>
    /// The schedule menu's one preset button today.
    /// </summary>
    /// <remarks>
    /// Named for the <c>ITaskService.RescheduleAsync</c> call <c>ScheduleAction</c> makes, not for
    /// the button that opens the menu -- that word belongs to <see cref="TaskNavigations.Schedule"/>,
    /// the main keyboard's own Schedule-labelled button, which acts on no task at all. This entry's
    /// own <see cref="TaskActionDefinition.Label"/> is "Reschedule" rather than "+1h" because a
    /// catalogue entry's label is meant for a developer reading this file, not for the button the
    /// owner sees; the button's own text is rendered straight from <see cref="PlusOneHour"/>, see
    /// <c>TelegramNotifier.BuildScheduleMenuKeyboard</c>.
    /// </remarks>
    public static TaskActionDefinition Reschedule { get; } = new(
        Key: "reschedule",
        Label: "Reschedule",
        Description: "Moves the task's due time to one hour from now, arming a reminder for the "
            + "first time on a task that had none. Refused when the task is already complete, or "
            + "when the callback carries an argument other than \"+1h\".");

    /// <summary>
    /// Every declared action, in declaration order.
    /// </summary>
    public static IReadOnlyList<TaskActionDefinition> All { get; } = [Done, Reschedule];
}
