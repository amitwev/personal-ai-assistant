namespace Assistant.Contracts;

/// <summary>
/// Request to create a task.
/// </summary>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="DueAtLocal">
/// Absolute local ISO-8601 datetime with no offset, for example <c>2026-08-17T10:00:00</c>.
/// <see langword="null"/> creates a task with no deadline, which never reminds.
/// </param>
/// <param name="Notes">Optional longer detail.</param>
/// <param name="IsHighPriority">Whether the task is raised in importance.</param>
public sealed record CreateTaskRequest(
    string Title,
    string? DueAtLocal = null,
    string? Notes = null,
    bool IsHighPriority = false);
