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

    /// <summary>
    /// The requested time is more than a minute in the past.
    /// </summary>
    DueTimeInPast,

    /// <summary>
    /// The requested time is more than two years ahead, which is far more likely a misread
    /// year than a real intention.
    /// </summary>
    DueTimeTooFarAhead,

    /// <summary>
    /// The chat model could not be reached, or it responded with an error.
    /// </summary>
    ModelUnavailable,

    /// <summary>
    /// The chat model was reached and answered, but returned no content and called no tools.
    /// </summary>
    ModelReturnedNoAnswer,

    /// <summary>
    /// The chat model was reached and answered, but without calling any tool.
    /// </summary>
    ModelReturnedNoToolCall,

    /// <summary>
    /// The task has already been completed.
    /// </summary>
    TaskAlreadyCompleted,
}
