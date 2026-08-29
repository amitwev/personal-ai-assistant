using System.Configuration;
using Assistant.Interfaces;

namespace Assistant.Impl.Settings;

/// <summary>
/// Configuration for the zone the assistant reads and writes wall-clock times in.
/// </summary>
/// <remarks>
/// One zone serves the whole assistant, because it serves one person. Per-user zones are
/// deferred (spec §12.7); binding this one from configuration rather than naming it in code is
/// not (spec §11.4) — a hardcoded zone would block every contributor outside Israel in their
/// first five minutes.
/// </remarks>
public sealed class TimeSettings : IValidatableConfig
{
    /// <summary>
    /// The IANA identifier of the assistant's zone, such as <c>Asia/Jerusalem</c>.
    /// </summary>
    public required string IanaTimeZone { get; init; }

    /// <inheritdoc/>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(IanaTimeZone))
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TimeSettings)}.{nameof(IanaTimeZone)} is missing or empty.");
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(IanaTimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ConfigurationErrorsException(
                $"{nameof(TimeSettings)}.{nameof(IanaTimeZone)} is '{IanaTimeZone}', which this "
                + "machine does not know. Use an IANA identifier such as 'Asia/Jerusalem'.", ex);
        }
    }
}
