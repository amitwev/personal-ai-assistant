namespace Assistant.Interfaces;

/// <summary>
/// The current instant, injected so time-dependent rules can be tested.
/// </summary>
public interface IClock
{
    /// <summary>
    /// The current instant in UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
