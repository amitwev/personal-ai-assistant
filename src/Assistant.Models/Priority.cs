namespace Assistant.Models;

/// <summary>
/// Relative importance of a <see cref="ReminderTask"/>.
/// </summary>
public enum Priority
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown,

    /// <summary>
    /// Default importance.
    /// </summary>
    Normal,

    /// <summary>
    /// Raised importance. Surfaced first in listings and briefs.
    /// </summary>
    High,
}
