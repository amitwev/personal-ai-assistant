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
    /// The Done button's definition.
    /// </summary>
    public static TaskActionDefinition Done { get; } = new(
        Key: "done",
        Label: "Done",
        Description: "Marks the task complete. Refused when the task is already complete.");

    /// <summary>
    /// Every declared action, in declaration order.
    /// </summary>
    public static IReadOnlyList<TaskActionDefinition> All { get; } = [Done];
}
