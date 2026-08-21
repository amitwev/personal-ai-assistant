namespace Assistant.Contracts;

/// <summary>
/// Which tasks a listing should return.
/// </summary>
public enum TaskFilter
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Pending tasks due at any point today, local time.
    /// </summary>
    Today = 1,

    /// <summary>
    /// Pending tasks whose due time has passed.
    /// </summary>
    Overdue = 2,

    /// <summary>
    /// Pending tasks due within the next seven days.
    /// </summary>
    Week = 3,

    /// <summary>
    /// All pending tasks, including those with no due time.
    /// </summary>
    All = 4,
}
