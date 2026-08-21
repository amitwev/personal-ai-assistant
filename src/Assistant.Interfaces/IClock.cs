namespace Assistant.Interfaces;

/// <summary>
/// Source of the current time.
/// </summary>
/// <remarks>
/// Every time-dependent rule in the system reads the clock through this interface, which is what
/// makes reminder scheduling, snoozing, and daylight-saving behaviour testable without waiting.
/// </remarks>
public interface IClock
{
    /// <summary>
    /// The current instant in UTC.
    /// </summary>
    /// <value>A <see cref="DateTimeOffset"/> whose offset is always <see cref="TimeSpan.Zero"/>.</value>
    DateTimeOffset UtcNow { get; }
}
