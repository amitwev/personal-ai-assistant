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

    /// <summary>
    /// A tool call's arguments could not be parsed as a JSON object at all. The model is not
    /// bound by a tool's declared schema, so this is reachable in practice, not only in theory.
    /// </summary>
    ToolArgumentsMalformed,

    /// <summary>
    /// A tool call's arguments parsed, but a field the tool requires was absent or blank.
    /// </summary>
    ToolArgumentMissing,

    /// <summary>
    /// A due time's text did not match the exact wall-clock shape the model is asked to supply,
    /// so no instant could be resolved from it at all.
    /// </summary>
    DueTimeUnparseable,

    /// <summary>
    /// The chat model called a tool that is not among those registered.
    /// </summary>
    ModelNamedUnknownTool,
}
