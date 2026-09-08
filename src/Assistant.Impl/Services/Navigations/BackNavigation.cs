using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Navigations;

/// <summary>
/// Closes the schedule menu in response to its Back button being tapped.
/// </summary>
internal sealed class BackNavigation : ITaskNavigation
{
    /// <inheritdoc/>
    public TaskNavigationDefinition Definition => TaskNavigations.Back;
}
