namespace Assistant.Contracts;

/// <summary>
/// Why an operation was rejected.
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// No error.
    /// </summary>
    None = 1,

    /// <summary>
    /// No task exists with the given identifier.
    /// </summary>
    TaskNotFound = 2,

    /// <summary>
    /// The task has already been completed, and the operation requires an open task.
    /// </summary>
    TaskAlreadyCompleted = 3,

    /// <summary>
    /// The task has been cancelled, and the operation requires an open task.
    /// </summary>
    TaskCancelled = 4,

    /// <summary>
    /// The operation requires a due time and the task has none.
    /// </summary>
    TaskHasNoDueTime = 5,

    /// <summary>
    /// The supplied time is in the past.
    /// </summary>
    TimeInPast = 6,

    /// <summary>
    /// The supplied time is implausibly far in the future.
    /// </summary>
    TimeTooFarAhead = 7,

    /// <summary>
    /// The supplied text could not be parsed as a local ISO-8601 datetime.
    /// </summary>
    TimeUnparseable = 8,

    /// <summary>
    /// Every language model provider failed.
    /// </summary>
    LlmUnavailable = 9,
}
