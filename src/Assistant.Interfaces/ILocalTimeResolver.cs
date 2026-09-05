using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// Parses wall-clock text in the assistant's configured zone and resolves it to the instant it
/// names.
/// </summary>
/// <remarks>
/// The model returns absolute local times with no offset (spec §5.4) as raw text, so this is
/// also where that text is parsed -- not a step a caller performs first. Something has to say
/// which zone a reading belongs to and what to do when the wall clock is not a reliable guide:
/// the hour that does not exist on a spring-forward night, the hour that happens twice on a
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
    /// Parses a wall-clock time in the configured zone and resolves it to the instant it names.
    /// </summary>
    /// <param name="local">
    /// The date and time as the user means it, in the exact form the model is asked to supply:
    /// ISO-8601 with no offset and no trailing zone designator, for example
    /// <c>2026-08-31T10:00:00</c>. Text that does not match this shape -- including text that is
    /// otherwise a valid date but carries an explicit offset or a trailing <c>Z</c> -- is refused
    /// rather than partially honoured: this project's times are always wall-clock readings with
    /// no instant of their own until this method assigns one, so an embedded offset would already
    /// be a claim about an instant, not a reading.
    /// </param>
    /// <returns>
    /// The instant, on UTC with a zero offset, or the reason it was refused:
    /// <see cref="ErrorCode.DueTimeUnparseable"/> when <paramref name="local"/> does not match
    /// the expected shape at all, <see cref="ErrorCode.DueTimeInPast"/> more than a minute before
    /// now, and <see cref="ErrorCode.DueTimeTooFarAhead"/> more than two years after it. A
    /// reading in a spring-forward gap resolves to the same reading past the gap; a reading in a
    /// fall-back hour resolves to the first of its two occurrences.
    /// </returns>
    Result<DateTimeOffset> Resolve(string local);
}
