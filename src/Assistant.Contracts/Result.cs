namespace Assistant.Contracts;

/// <summary>
/// The outcome of an operation that either succeeds or is refused for a stated reason.
/// </summary>
/// <param name="Error">The reason it was refused, or <see langword="null"/> when it succeeded.</param>
/// <remarks>
/// The reason is nullable rather than defaulting to <see cref="ErrorCode.Unknown"/>: every enum in
/// this project reserves its first member for "nobody set this", so a success carrying
/// <c>Unknown</c> would read as a failure whose cause was lost.
/// </remarks>
public readonly record struct Result(ErrorCode? Error)
{
    /// <summary>
    /// Whether the operation was carried out.
    /// </summary>
    public bool IsSuccess => Error is null;

    /// <summary>
    /// The outcome of an operation that was carried out.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new((ErrorCode?)null);

    /// <summary>
    /// The outcome of an operation that was refused.
    /// </summary>
    /// <param name="error">Why it was refused.</param>
    /// <returns>A failed result carrying the reason.</returns>
    public static Result Failure(ErrorCode error) => new(error);
}
