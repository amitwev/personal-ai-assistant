namespace Assistant.Models;

/// <summary>
/// Lifecycle state of a <see cref="ReminderTask"/>.
/// </summary>
public enum ReminderStatus
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown,

    /// <summary>
    /// Outstanding. Eligible for reminder delivery.
    /// </summary>
    Pending,

    /// <summary>
    /// Finished. No further reminders are delivered.
    /// </summary>
    Completed,

    /// <summary>
    /// Abandoned without being done. No further reminders are delivered.
    /// </summary>
    Cancelled,
}
