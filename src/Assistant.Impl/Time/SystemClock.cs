using Assistant.Interfaces;

namespace Assistant.Impl.Time;

/// <summary>
/// The real clock.
/// </summary>
internal sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
