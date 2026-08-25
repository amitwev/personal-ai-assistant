using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for <see cref="INotifier"/>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(WireMockCollection.Name)]
public sealed class TelegramNotifierTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;

    private ServiceProvider _provider = null!;

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken,
            OwnerChatId = OwnerChatId,
            BaseUrl = wireMock.Url,
        });
        _provider = services.BuildServiceProvider();
        return wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a message is sent
    /// And its text is either plain or contains characters MarkdownV2 would treat as formatting
    /// Then exactly one request reaches Telegram, addressed to the owner, with the text unchanged.
    /// </summary>
    [Theory]
    [InlineData("Call the bank")]
    [InlineData("Call the bank_now *urgent*")]
    public async Task SendAsync_Text_PostsOneMessageToTheOwner(string text)
    {
        // Arrange
        var expected = new SendMessagePayload(OwnerChatId, text, "Html");
        var sut = _provider.GetRequiredService<INotifier>();

        // Act
        await sut.SendAsync(text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(await wireMock.SentMessagesAsync()), strict: true);
    }
}
