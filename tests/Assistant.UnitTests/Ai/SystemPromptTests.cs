using System.Globalization;
using Assistant.Impl.Ai;
using Assistant.Impl.Services;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Ai;

/// <summary>
/// Test class for <see cref="SystemPrompt"/>.
/// </summary>
public sealed class SystemPromptTests
{
    /// <summary>
    /// When the prompt is built for a round-hour offset
    /// Then it states the exact current time, the zone, and the offset with no minutes shown.
    /// </summary>
    [Fact]
    public void Build_JerusalemInAugust_StatesTheExactCurrentTime()
    {
        // Arrange
        var prompt = PromptIn("Asia/Jerusalem", "2026-08-16T20:40:00Z");

        // Act
        var text = prompt.Build();

        // Assert
        Assert.Contains("Sunday 16 August 2026, 23:40, Asia/Jerusalem (UTC+3)", text);
    }

    /// <summary>
    /// When the prompt is built for a half-hour offset
    /// Then the offset is rendered with minutes, not rounded away.
    /// </summary>
    /// <remarks>
    /// Lord Howe's one-off half-hour daylight shift runs 2026-10-04 to 2026-04-05 (F8's verified
    /// table). 2026-08-16 falls outside that window, so the zone is on its year-round base
    /// offset, standard time, UTC+10:30 -- not the shifted UTC+11.
    /// </remarks>
    [Fact]
    public void Build_LordHoweOffsetIsNotARoundHour_RendersTheMinutes()
    {
        // Arrange
        var prompt = PromptIn("Australia/Lord_Howe", "2026-08-16T20:40:00Z");

        // Act
        var text = prompt.Build();

        // Assert
        Assert.Contains("Monday 17 August 2026, 07:10, Australia/Lord_Howe (UTC+10:30)", text);
    }

    private static SystemPrompt PromptIn(string zoneId, string utcNow) =>
        new(new LocalTimeResolver(
            TimeZoneInfo.FindSystemTimeZoneById(zoneId),
            new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture))));
}
