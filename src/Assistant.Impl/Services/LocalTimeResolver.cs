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
        var instant = new DateTimeOffset(wall, zone.GetUtcOffset(wall)).ToUniversalTime();
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
