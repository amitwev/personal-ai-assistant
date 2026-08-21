namespace Assistant.Contracts;

/// <summary>
/// Why an operation was rejected.
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// Unset default. Never valid to persist or act on.
    /// </summary>
    Unknown,

    /// <summary>
    /// No error.
    /// </summary>
    None,

    /// <summary>
    /// No task exists with the given identifier.
    /// </summary>
    TaskNotFound,

    /// <summary>
    /// The task has already been completed, and the operation requires an open task.
    /// </summary>
    TaskAlreadyCompleted,

    /// <summary>
    /// The task has been cancelled, and the operation requires an open task.
    /// </summary>
    TaskCancelled,

    /// <summary>
    /// The operation requires a due time and the task has none.
    /// </summary>
    TaskHasNoDueTime,

    /// <summary>
    /// The supplied time is in the past.
    /// </summary>
    TimeInPast,

    /// <summary>
    /// The supplied time is implausibly far in the future.
    /// </summary>
    TimeTooFarAhead,

    /// <summary>
    /// The supplied text could not be parsed as a local ISO-8601 datetime.
    /// </summary>
    TimeUnparseable,

    /// <summary>
    /// Every language model provider failed.
    /// </summary>
    LlmUnavailable,
}
