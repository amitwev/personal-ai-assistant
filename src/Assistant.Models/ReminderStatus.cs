namespace Assistant.Models;

/// <summary>
/// Lifecycle state of a <see cref="ReminderTask"/>.
/// </summary>
public enum ReminderStatus
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Outstanding. Eligible for reminder delivery.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Finished. No further reminders are delivered.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Abandoned without being done. No further reminders are delivered.
    /// </summary>
    Cancelled = 3,
}
