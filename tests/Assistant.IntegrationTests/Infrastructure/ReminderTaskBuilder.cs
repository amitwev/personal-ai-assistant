using Assistant.Models;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Shared factory for <see cref="ReminderTask"/> instances used across the integration suite.
/// </summary>
internal static class ReminderTaskBuilder
{
    /// <summary>
    /// Builds a reminder task with a fixed title and creation instant, so a test overrides only
    /// the fields its scenario cares about.
    /// </summary>
    /// <param name="dueAt">The due instant, or null when the task has none. Defaults to null.</param>
    /// <param name="status">The task's status. Defaults to <see cref="ReminderStatus.Pending"/>.</param>
    /// <param name="reminderSentAt">
    /// The instant the reminder was sent, or null when it has not been. Defaults to null.
    /// </param>
    /// <param name="completedAt">
    /// The instant the task was completed, or null when it has not been. Defaults to null.
    /// </param>
    /// <returns>A new <see cref="ReminderTask"/> with a freshly generated identifier.</returns>
    internal static ReminderTask BuildReminderTask(
        DateTimeOffset? dueAt = null,
        ReminderStatus status = ReminderStatus.Pending,
        DateTimeOffset? reminderSentAt = null,
        DateTimeOffset? completedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "call the bank",
        Status = status,
        DueAt = dueAt,
        ReminderSentAt = reminderSentAt,
        CreatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        CompletedAt = completedAt,
    };
}
