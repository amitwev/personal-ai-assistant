namespace Assistant.Contracts;

/// <summary>
/// Request to list tasks.
/// </summary>
/// <param name="Filter">Which tasks to return.</param>
/// <param name="Limit">Maximum number of tasks to return. Clamped to 100 by the service.</param>
public sealed record ListTasksRequest(TaskFilter Filter = TaskFilter.Today, int Limit = 20);
