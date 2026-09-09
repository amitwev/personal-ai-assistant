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
    /// The argument the schedule button carries on the wire, and the only one
    /// <c>ScheduleAction</c> understands -- shared here so the button that sends it and the
    /// action that accepts it can never drift apart.
    /// </summary>
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
    /// The label is still "+1h": that is still the only preset <c>ScheduleAction</c> recognises,
    /// and "+1h" is still the exact text a tap on this button applies -- only its position moved,
    /// from the main keyboard into the schedule menu <see cref="TaskNavigations.OpenSchedule"/>
    /// now opens. The main keyboard's own Schedule-labelled button is that navigation, not this
    /// action -- see <see cref="TaskNavigations"/>. Once a second preset is registered, each one
    /// will carry its own label from a dedicated catalogue, and this field's role will need a
    /// second look then.
    /// </remarks>
    public static TaskActionDefinition Schedule { get; } = new(
        Key: "schedule",
        Label: "+1h",
        Description: "Moves the task's due time to one hour from now, arming a reminder for the "
            + "first time on a task that had none. Refused when the task is already complete, or "
            + "when the callback carries an argument other than \"+1h\".");

    /// <summary>
    /// Every declared action, in declaration order.
    /// </summary>
    public static IReadOnlyList<TaskActionDefinition> All { get; } = [Done, Schedule];
}
