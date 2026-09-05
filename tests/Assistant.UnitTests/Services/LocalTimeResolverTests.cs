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
        var result = resolver.Resolve(local);

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
        var result = resolver.Resolve("2026-08-17T10:00:00");

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
        var result = resolver.Resolve("2026-08-17T09:58:00");

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
        var result = resolver.Resolve("2026-08-17T09:59:00");

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
        var result = resolver.Resolve("2028-08-17T10:01:00");

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
        var result = resolver.Resolve("2028-08-17T10:00:00");

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// When the time given falls in the hour a spring-forward night skips
    /// And it is resolved
    /// Then it names the instant that same reading names past the gap.
    /// </summary>
    /// <param name="zoneId">The zone whose clocks move.</param>
    /// <param name="local">A reading inside the gap.</param>
    /// <param name="expectedUtc">The instant it names.</param>
    /// <remarks>
    /// Lord Howe Island is here because its gap is half an hour wide. Israel's is a full hour,
    /// which is the same width as the offset change, so an implementation that confuses the two
    /// cannot be caught in Israel alone.
    /// </remarks>
    [Theory]
    [InlineData("Asia/Jerusalem", "2026-03-27T02:30:00", "2026-03-27T00:30:00Z")]
    [InlineData("Australia/Lord_Howe", "2026-10-04T02:15:00", "2026-10-03T15:45:00Z")]
    public void Resolve_TimeInsideASpringForwardGap_NamesTheInstantPastTheGap(
        string zoneId, string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverIn(zoneId, "2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(local);

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }

    /// <summary>
    /// When the time given falls in the hour a fall-back night repeats
    /// And it is resolved
    /// Then it lands on the first of the two occurrences.
    /// </summary>
    /// <param name="zoneId">The zone whose clocks move.</param>
    /// <param name="local">A reading inside the repeated hour.</param>
    /// <param name="expectedUtc">The instant of its first occurrence.</param>
    [Theory]
    [InlineData("Asia/Jerusalem", "2026-10-25T01:30:00", "2026-10-24T22:30:00Z")]
    [InlineData("Australia/Lord_Howe", "2026-04-05T01:45:00", "2026-04-04T14:45:00Z")]
    public void Resolve_TimeInsideAFallBackHour_TakesTheFirstOccurrence(
        string zoneId, string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverIn(zoneId, "2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(local);

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }

    /// <summary>
    /// When the time given sits either side of a clock change without touching it
    /// And it is resolved
    /// Then it is left exactly where it is.
    /// </summary>
    /// <param name="local">A reading just outside a transition.</param>
    /// <param name="expectedUtc">The instant it names.</param>
    [Theory]
    [InlineData("2026-03-27T01:30:00", "2026-03-26T23:30:00Z")]
    [InlineData("2026-03-27T03:30:00", "2026-03-27T00:30:00Z")]
    [InlineData("2026-10-25T00:30:00", "2026-10-24T21:30:00Z")]
    [InlineData("2026-10-25T02:30:00", "2026-10-25T00:30:00Z")]
    public void Resolve_TimeEitherSideOfAClockChange_IsUnmoved(
        string local, string expectedUtc)
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(local);

        // Assert
        Assert.Equal(Instant(expectedUtc), result.Value);
    }

    /// <summary>
    /// When a due time's text does not match the exact wall-clock shape the model is asked to
    /// supply
    /// And it is resolved
    /// Then it is refused, whether the text is nonsense or merely names an instant of its own.
    /// </summary>
    /// <param name="local">Text that is not a bare wall-clock reading.</param>
    /// <remarks>
    /// A trailing <c>Z</c> or an explicit offset is deliberately refused rather than stripped and
    /// honoured: this project's times are always wall-clock readings with no instant of their
    /// own until this method assigns one, so text that already claims an instant is a different
    /// shape entirely, not a lenient variant of the expected one.
    /// </remarks>
    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    [InlineData("2026-08-17")]
    [InlineData("2026-08-17T10:00:00Z")]
    [InlineData("2026-08-17T10:00:00+02:00")]
    public void Resolve_TextDoesNotMatchTheExpectedShape_IsRefused(string local)
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var result = resolver.Resolve(local);

        // Assert
        Assert.Equal(ErrorCode.DueTimeUnparseable, result.Error);
    }

    /// <summary>
    /// When the current instant is read
    /// Then it carries the offset in force in the configured zone at that instant.
    /// </summary>
    [Fact]
    public void CurrentLocalTime_AnyInstant_CarriesTheZonesOffsetAtThatInstant()
    {
        // Arrange
        var resolver = ResolverAt("2026-08-16T20:40:00Z");

        // Act
        var now = resolver.CurrentLocalTime;

        // Assert
        Assert.Equal(Instant("2026-08-16T20:40:00Z"), now);
        Assert.Equal(TimeSpan.FromHours(3), now.Offset);
    }

    /// <summary>
    /// When the zone identifier is read
    /// Then it is the identifier the resolver was constructed with.
    /// </summary>
    [Fact]
    public void ZoneId_AnyResolver_IsTheConfiguredZonesIdentifier()
    {
        // Arrange
        var resolver = ResolverIn("Australia/Lord_Howe", "2026-08-16T20:40:00Z");

        // Act
        var zoneId = resolver.ZoneId;

        // Assert
        Assert.Equal("Australia/Lord_Howe", zoneId);
    }

    /// <summary>
    /// When a stored UTC instant is converted back to local text
    /// Then it carries the wall-clock reading and offset in force in the configured zone at
    /// that instant.
    /// </summary>
    [Fact]
    public void ToLocal_AnyInstant_ReturnsTheWallClockReadingInTheConfiguredZone()
    {
        // Arrange
        var resolver = ResolverAt("2026-01-01T00:00:00Z");

        // Act
        var local = resolver.ToLocal(Instant("2026-08-26T07:00:00Z"));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.FromHours(3)), local);
    }

    private static LocalTimeResolver ResolverIn(string zoneId, string utcNow) =>
        new(TimeZoneInfo.FindSystemTimeZoneById(zoneId), new FakeTimeProvider(Instant(utcNow)));

    private static LocalTimeResolver ResolverAt(string utcNow) =>
        ResolverIn("Asia/Jerusalem", utcNow);

    private static DateTimeOffset Instant(string utc) =>
        DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
}
