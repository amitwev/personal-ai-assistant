using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Navigations;

/// <summary>
/// Opens the schedule menu in response to the main keyboard's Schedule button being tapped.
/// </summary>
internal sealed class OpenScheduleNavigation : ITaskNavigation
{
    /// <inheritdoc/>
    public TaskNavigationDefinition Definition => TaskNavigations.OpenSchedule;
}
