using System.Globalization;
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services;

/// <summary>
/// Resolves wall-clock text against the single zone the assistant is configured for.
/// </summary>
/// <param name="zone">The zone every wall-clock time is read in.</param>
/// <param name="timeProvider">The clock the past and future guards judge against.</param>
internal sealed class LocalTimeResolver(TimeZoneInfo zone, TimeProvider timeProvider)
    : ILocalTimeResolver
{
    private const string WallClockFormat = "yyyy-MM-ddTHH:mm:ss";

    private static readonly TimeSpan PastTolerance = TimeSpan.FromMinutes(1);

    private const int MaxYearsAhead = 2;

    /// <inheritdoc/>
    public DateTimeOffset CurrentLocalTime =>
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);

    /// <inheritdoc/>
    public string ZoneId => zone.Id;

    /// <inheritdoc/>
    public Result<DateTimeOffset> Resolve(string local)
    {
        if (!DateTime.TryParseExact(
                local, WallClockFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            return Result<DateTimeOffset>.Failure(ErrorCode.DueTimeUnparseable);
        }

        // Falling back always lowers the offset, so the larger of an ambiguous reading's two
        // offsets is its first occurrence. GetUtcOffset and ConvertTimeToUtc both hand back the
        // second. A reading inside a spring-forward gap needs no such handling: GetUtcOffset
        // returns the offset in force before the gap, which names the same instant as the same
        // reading past it, whatever the gap's width.
        var offset = zone.IsAmbiguousTime(wall)
            ? zone.GetAmbiguousTimeOffsets(wall).Max()
            : zone.GetUtcOffset(wall);

        var instant = new DateTimeOffset(wall, offset).ToUniversalTime();
        var now = timeProvider.GetUtcNow();

        if (instant < now - PastTolerance)
        {
            return Result<DateTimeOffset>.Failure(ErrorCode.DueTimeInPast);
        }

        if (instant > now.AddYears(MaxYearsAhead))
        {
            return Result<DateTimeOffset>.Failure(ErrorCode.DueTimeTooFarAhead);
        }

        return Result<DateTimeOffset>.Success(instant);
    }

    /// <inheritdoc/>
    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, zone);
}
