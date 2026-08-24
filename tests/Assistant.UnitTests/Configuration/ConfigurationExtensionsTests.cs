using System.Configuration;
using Assistant.Impl.Configuration;
using Assistant.Impl.Settings;
using Microsoft.Extensions.Configuration;

namespace Assistant.UnitTests.Configuration;

/// <summary>
/// Test class for <see cref="Assistant.Impl.Configuration.ConfigurationExtensions.Read{T}"/>.
/// </summary>
public sealed class ConfigurationExtensionsTests
{
    private const string BotToken = "123456:TESTTOKEN";
    private const string OwnerChatId = "472619570";

    /// <summary>
    /// When the settings section is absent altogether
    /// And configuration is read
    /// Then startup fails rather than continuing with defaults.
    /// </summary>
    [Fact]
    public void Read_SectionMissing_Throws()
    {
        // Arrange
        var configuration = BuildConfiguration([]);

        // Act
        var exception = Record.Exception(() => configuration.Read<TelegramSettings>());

        // Assert
        var error = Assert.IsType<ConfigurationErrorsException>(exception);
        Assert.Contains("was not found", error.Message);
    }

    /// <summary>
    /// When a mandatory value is absent from an otherwise present section
    /// And configuration is read
    /// Then startup fails rather than binding it to null or zero.
    /// </summary>
    [Theory]
    [InlineData("TelegramSettings:OwnerChatId", OwnerChatId)]
    [InlineData("TelegramSettings:BotToken", BotToken)]
    public void Read_MandatoryValueMissing_Throws(string presentKey, string presentValue)
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [presentKey] = presentValue,
        });

        // Act
        var exception = Record.Exception(() => configuration.Read<TelegramSettings>());

        // Assert
        Assert.IsType<ConfigurationErrorsException>(exception);
    }

    /// <summary>
    /// When every mandatory value is present
    /// And configuration is read
    /// Then the settings are returned exactly as configured.
    /// </summary>
    [Fact]
    public void Read_EveryMandatoryValuePresent_ReturnsSettings()
    {
        // Arrange
        var expected = new TelegramSettings
        {
            BotToken = BotToken,
            OwnerChatId = 472619570L,
            BaseUrl = "http://localhost:58080",
        };
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["TelegramSettings:BotToken"] = BotToken,
            ["TelegramSettings:OwnerChatId"] = OwnerChatId,
            ["TelegramSettings:BaseUrl"] = "http://localhost:58080",
        });

        // Act
        var result = configuration.Read<TelegramSettings>();

        // Assert
        Assert.Equivalent(expected, result, strict: true);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
