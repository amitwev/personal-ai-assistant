namespace Assistant.Models;

/// <summary>
/// Relative importance of a <see cref="ReminderTask"/>.
/// </summary>
public enum Priority
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Default importance.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Raised importance. Surfaced first in listings and briefs.
    /// </summary>
    High = 2,
}
