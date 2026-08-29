using System.Globalization;
using Assistant.Contracts;
using Assistant.Impl.Services;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Services;

/// <summary>
/// Test class for <see cref="LocalTimeResolver"/>.
/// </summary>
public sealed class LocalTimeResolverTests
{
    private static readonly TimeZoneInfo Jerusalem =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    /// <summary>
    /// When a due time names a reading of the wall clock in the configured zone
    /// And it is resolved
    /// Then the instant carries the offset in force on that date, summer or winter.
    /// </summary>
    /// <param name="local">The wall-clock reading the user meant.</param>
    /// <param name="expectedUtc">The instant it names.</param>
    [Theory]
    [InlineData("2026-08-17T10:00:00", "2026-08-17T07:00:00Z")]
    [InlineData("2026-01-15T10:00:00", "2026-01-15T08:00:00Z")]
    public void Resolve_TimeInEitherSeason_ReturnsTheInstantThatReadingNames(
        string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(Wall(local));

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }

    /// <summary>
    /// When a time is resolved
    /// Then it comes back on UTC rather than on the zone's own offset.
    /// </summary>
    /// <remarks>
    /// Every instant this project stores is UTC with a zero offset, and
    /// <see cref="DateTimeOffset"/> equality compares points in time regardless of offset — so
    /// without this, no other assertion in the file would notice the offset drifting.
    /// </remarks>
    [Fact]
    public void Resolve_AnyTime_ReturnsTheInstantOnUtc()
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2026-08-17T10:00:00"));

        // Assert
        Assert.Equal(TimeSpan.Zero, result.Value.Offset);
    }

    /// <summary>
    /// When the time given has already passed by more than a minute
    /// And it is resolved
    /// Then it is refused, so the assistant can ask instead of reminding at once.
    /// </summary>
    [Fact]
    public void Resolve_MoreThanAMinuteInThePast_IsRefused()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2026-08-17T09:58:00"));

        // Assert
        Assert.Equal(ErrorCode.DueTimeInPast, result.Error);
    }

    /// <summary>
    /// When the time given is exactly one minute old
    /// And it is resolved
    /// Then it is accepted, because only more than a minute is refused.
    /// </summary>
    [Fact]
    public void Resolve_ExactlyOneMinuteInThePast_IsAccepted()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2026-08-17T09:59:00"));

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// When the time given is more than two years ahead
    /// And it is resolved
    /// Then it is refused, because a misread year is likelier than the intention.
    /// </summary>
    [Fact]
    public void Resolve_MoreThanTwoYearsAhead_IsRefused()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2028-08-17T10:01:00"));

        // Assert
        Assert.Equal(ErrorCode.DueTimeTooFarAhead, result.Error);
    }

    /// <summary>
    /// When the time given is exactly two years ahead
    /// And it is resolved
    /// Then it is accepted, because only more than two years is refused.
    /// </summary>
    [Fact]
    public void Resolve_ExactlyTwoYearsAhead_IsAccepted()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-17T07:00:00Z");

        // Act
        var result = resolver.Resolve(Wall("2028-08-17T10:00:00"));

        // Assert
        Assert.True(result.IsSuccess);
    }

    private static LocalTimeResolver ResolverAt(string utcNow) =>
        new(Jerusalem, new FakeTimeProvider(Instant(utcNow)));

    private static DateTime Wall(string local) =>
        DateTime.Parse(local, CultureInfo.InvariantCulture);

    private static DateTimeOffset Instant(string utc) =>
        DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
}
