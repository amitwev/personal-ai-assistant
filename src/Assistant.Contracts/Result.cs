namespace Assistant.Contracts;

/// <summary>
/// Outcome of an operation that can fail for an expected reason.
/// </summary>
/// <remarks>
/// Expected failures — a missing task, a time in the past — are returned rather than thrown.
/// Exceptions remain for genuine faults such as a database being unreachable.
/// </remarks>
public sealed class Result
{
    private Result(bool succeeded, ErrorCode error, string? message)
    {
        Succeeded = succeeded;
        Error = error;
        Message = message;
    }

    /// <summary>
    /// Whether the operation completed.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Why the operation was rejected.
    /// </summary>
    /// <value><see cref="ErrorCode.None"/> when <see cref="Succeeded"/> is <see langword="true"/>.</value>
    public ErrorCode Error { get; }

    /// <summary>
    /// Human-readable explanation suitable for showing to the user.
    /// </summary>
    /// <value><see langword="null"/> on success.</value>
    public string? Message { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A result whose <see cref="Succeeded"/> is <see langword="true"/>.</returns>
    public static Result Success() => new(true, ErrorCode.None, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">Why the operation was rejected.</param>
    /// <param name="message">Explanation suitable for showing to the user.</param>
    /// <returns>A result whose <see cref="Succeeded"/> is <see langword="false"/>.</returns>
    public static Result Failure(ErrorCode error, string message) => new(false, error, message);
}
