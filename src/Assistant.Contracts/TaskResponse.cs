namespace Assistant.Contracts;

/// <summary>
/// A task as presented to a caller.
/// </summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="Notes">Longer detail, if any.</param>
/// <param name="DueAtLocal">
/// Due time rendered in local time, or <see langword="null"/> when the task has no deadline.
/// </param>
/// <param name="IsOverdue">Whether the due time has passed and the task is still pending.</param>
/// <param name="IsHighPriority">Whether the task is raised in importance.</param>
/// <param name="IsCompleted">Whether the task has been completed.</param>
public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Notes,
    DateTimeOffset? DueAtLocal,
    bool IsOverdue,
    bool IsHighPriority,
    bool IsCompleted);
