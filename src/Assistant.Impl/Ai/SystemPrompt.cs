using System.Globalization;
using Assistant.Interfaces;

namespace Assistant.Impl.Ai;

/// <summary>
/// Builds the system prompt sent as the first message on every call to the chat model.
/// </summary>
/// <param name="clock">Supplies the current time and the zone it is read in.</param>
/// <remarks>
/// The zone is read from <see cref="ILocalTimeResolver.ZoneId"/> rather than named as a literal,
/// and it appears twice in the built text so that editing either mention into a hardcoded zone
/// leaves the other visibly disagreeing with it.
/// </remarks>
internal sealed class SystemPrompt(ILocalTimeResolver clock)
{
    /// <summary>
    /// Builds the prompt text for the current instant.
    /// </summary>
    /// <returns>
    /// The current time in the configured zone, that zone's identifier named twice, and the two
    /// instructions the model needs to answer with an absolute local time.
    /// </returns>
    public string Build() =>
        $"Current time: {clock.CurrentLocalTime.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}, "
        + $"{clock.ZoneId} ({FormatOffset(clock.CurrentLocalTime.Offset)}). "
        + $"All times the user gives are {clock.ZoneId} local. "
        + "Return absolute local ISO-8601 datetimes with no offset.";

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return magnitude.Minutes == 0
            ? $"UTC{sign}{magnitude.Hours}"
            : $"UTC{sign}{magnitude.Hours}:{magnitude.Minutes:00}";
    }
}
