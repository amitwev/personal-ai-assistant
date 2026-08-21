namespace Assistant.Contracts;

/// <summary>
/// A reminder ready to be delivered to the user.
/// </summary>
/// <remarks>
/// Carries no persistence shape, so the messaging layer never depends on the database schema.
/// </remarks>
/// <param name="TaskId">Identifier of the task, used to build the button callback payloads.</param>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="DueAtLocal">Due time rendered in local time.</param>
/// <param name="OverdueBy">
/// How long the task has been overdue, or <see langword="null"/> when it is due now.
/// </param>
public sealed record ReminderNotification(
    Guid TaskId,
    string Title,
    DateTimeOffset DueAtLocal,
    TimeSpan? OverdueBy);
