using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Turns a wall-clock time in the assistant's configured zone into the instant it names.
/// </summary>
/// <remarks>
/// The model returns absolute local times with no offset (spec §5.4), so something has to say
/// which zone they belong to and what to do when the wall clock is not a reliable guide: the
/// hour that does not exist on a spring-forward night, the hour that happens twice on a
/// fall-back night, and times so far from now that the model has most likely misread the date.
/// </remarks>
public interface ILocalTimeResolver
{
    /// <summary>
    /// The current instant, expressed as a wall-clock reading in the configured zone.
    /// </summary>
    /// <value>
    /// Read fresh from the injected clock on every access, so a caller driving a
    /// <c>FakeTimeProvider</c> sees an advance without re-resolving anything.
    /// </value>
    DateTimeOffset CurrentLocalTime { get; }

    /// <summary>
    /// The IANA identifier of the zone every wall-clock time on this assistant is read in.
    /// </summary>
    /// <value>
    /// The same identifier <c>TimeSettings.IanaTimeZone</c> was bound from at startup.
    /// </value>
    string ZoneId { get; }

    /// <summary>
    /// Resolves a wall-clock time in the configured zone to the instant it names.
    /// </summary>
    /// <param name="local">
    /// The date and time as the user means it. Any <see cref="DateTimeKind"/> is read as a
    /// wall-clock time in the configured zone, never as an instant.
    /// </param>
    /// <returns>
    /// The instant, on UTC with a zero offset, or the reason it was refused:
    /// <see cref="ErrorCode.DueTimeInPast"/> more than a minute before now, and
    /// <see cref="ErrorCode.DueTimeTooFarAhead"/> more than two years after it. A time in a
    /// spring-forward gap resolves to the same wall-clock reading past the gap; a time in a
    /// fall-back hour resolves to the first of its two occurrences.
    /// </returns>
    Result<DateTimeOffset> Resolve(DateTime local);
}
