namespace Assistant.Contracts;

/// <summary>
/// Why an operation was refused.
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// Unset default. Never valid to return.
    /// </summary>
    Unknown,

    /// <summary>
    /// No task carries the requested identifier.
    /// </summary>
    TaskNotFound,

    /// <summary>
    /// The task has no due time, so there is no reminder to act on.
    /// </summary>
    DueTimeMissing,
}
