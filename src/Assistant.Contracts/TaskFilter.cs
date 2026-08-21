namespace Assistant.Contracts;

/// <summary>
/// Which tasks a listing should return.
/// </summary>
public enum TaskFilter
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown,

    /// <summary>
    /// Pending tasks due at any point today, local time.
    /// </summary>
    Today,

    /// <summary>
    /// Pending tasks whose due time has passed.
    /// </summary>
    Overdue,

    /// <summary>
    /// Pending tasks due within the next seven days.
    /// </summary>
    Week,

    /// <summary>
    /// All pending tasks, including those with no due time.
    /// </summary>
    All,
}
