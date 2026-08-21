namespace Assistant.Models;

/// <summary>
/// Record that the daily brief was sent for a given local date.
/// </summary>
/// <remarks>
/// <see cref="BriefDate"/> is the primary key, which makes the insert itself the once-per-day
/// check: a duplicate send is a primary key violation rather than a race to be reasoned about.
/// </remarks>
public sealed class DailyBriefLog
{
    /// <summary>
    /// The local date the brief covered.
    /// </summary>
    public DateOnly BriefDate { get; set; }

    /// <summary>
    /// When the brief was actually delivered, in UTC.
    /// </summary>
    public DateTimeOffset SentAt { get; set; }
}
