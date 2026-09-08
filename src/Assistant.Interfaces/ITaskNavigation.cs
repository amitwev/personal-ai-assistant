using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// One inline button that swaps which keyboard a task message shows, without acting on the task.
/// </summary>
/// <remarks>
/// Resolved by <c>CallbackRouter</c> matching <see cref="Definition"/>'s
/// <see cref="TaskNavigationDefinition.Key"/> against the decoded callback action segment -- the
/// identical registration seam <see cref="ITaskAction"/> already is. Carries no method, only
/// <see cref="Definition"/>: it exists so a future navigation is a class plus a registration,
/// rather than a new case inside <c>CallbackRouter</c> itself.
/// </remarks>
public interface ITaskNavigation
{
    /// <summary>
    /// This navigation's entry in the shared catalogue.
    /// </summary>
    /// <value>Key, label, the keyboard it shows, and description all come from <see cref="TaskNavigations"/>.</value>
    TaskNavigationDefinition Definition { get; }
}
