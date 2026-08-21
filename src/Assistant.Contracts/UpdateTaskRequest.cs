namespace Assistant.Contracts;

/// <summary>
/// Request to change an existing task. Omitted fields are left unchanged.
/// </summary>
/// <param name="TaskId">Identifier of the task to change.</param>
/// <param name="Title">New description, or <see langword="null"/> to keep the current one.</param>
/// <param name="DueAtLocal">
/// New absolute local ISO-8601 datetime with no offset, or <see langword="null"/> to keep the
/// current due time. Changing it re-arms the reminder.
/// </param>
/// <param name="Notes">New detail, or <see langword="null"/> to keep the current text.</param>
/// <param name="IsHighPriority">New importance, or <see langword="null"/> to keep the current value.</param>
public sealed record UpdateTaskRequest(
    Guid TaskId,
    string? Title = null,
    string? DueAtLocal = null,
    string? Notes = null,
    bool? IsHighPriority = null);
