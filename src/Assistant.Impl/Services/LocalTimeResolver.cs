using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services;

/// <summary>
/// Resolves wall-clock times against the single zone the assistant is configured for.
/// </summary>
/// <param name="zone">The zone every wall-clock time is read in.</param>
/// <param name="timeProvider">The clock the past and future guards judge against.</param>
internal sealed class LocalTimeResolver(TimeZoneInfo zone, TimeProvider timeProvider)
    : ILocalTimeResolver
{
    private static readonly TimeSpan PastTolerance = TimeSpan.FromMinutes(1);

    private const int MaxYearsAhead = 2;

    /// <inheritdoc/>
    public Result<DateTimeOffset> Resolve(DateTime local)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
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
}
