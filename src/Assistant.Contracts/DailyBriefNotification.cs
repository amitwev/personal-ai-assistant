namespace Assistant.Contracts;

/// <summary>
/// The daily brief, ready to be delivered to the user.
/// </summary>
/// <param name="BriefDate">The local date the brief covers.</param>
/// <param name="DueToday">Tasks due at some point today.</param>
/// <param name="Overdue">Tasks whose due time has already passed.</param>
/// <param name="OpenWithoutDueDate">How many pending tasks have no deadline at all.</param>
public sealed record DailyBriefNotification(
    DateOnly BriefDate,
    IReadOnlyList<TaskResponse> DueToday,
    IReadOnlyList<TaskResponse> Overdue,
    int OpenWithoutDueDate);
