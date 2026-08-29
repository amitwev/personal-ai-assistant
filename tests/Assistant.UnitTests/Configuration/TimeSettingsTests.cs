using System.Configuration;
using Assistant.Impl.Configuration;
using Assistant.Impl.Settings;
using Microsoft.Extensions.Configuration;

namespace Assistant.UnitTests.Configuration;

/// <summary>
/// Test class for <see cref="TimeSettings"/>.
/// </summary>
public sealed class TimeSettingsTests
{
    /// <summary>
    /// When the configured zone is not an identifier this machine knows
    /// And configuration is read
    /// Then startup fails, naming the value that was wrong.
    /// </summary>
    [Fact]
    public void Read_ZoneIsNotAKnownIdentifier_Throws()
    {
        // Arrange
        var configuration = BuildConfiguration("Asia/Jerusalum");

        // Act
        var exception = Record.Exception(() => configuration.Read<TimeSettings>());

        // Assert
        var error = Assert.IsType<ConfigurationErrorsException>(exception);
        Assert.Contains("Asia/Jerusalum", error.Message);
    }

    /// <summary>
    /// When the configured zone is the one the repository ships as its default
    /// And configuration is read
    /// Then it is accepted.
    /// </summary>
    [Fact]
    public void Read_ZoneIsAKnownIdentifier_ReturnsSettings()
    {
        // Arrange
        var configuration = BuildConfiguration("Asia/Jerusalem");

        // Act
        var settings = configuration.Read<TimeSettings>();

        // Assert
        Assert.Equal("Asia/Jerusalem", settings.IanaTimeZone);
    }

    private static IConfiguration BuildConfiguration(string zone) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TimeSettings:IanaTimeZone"] = zone,
            })
            .Build();
}
