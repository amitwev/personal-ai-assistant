using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for the inbound listener registered via <c>AddAssistantListener</c>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class TelegramListenerTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;
    private const long StrangerChatId = 999888777L;

    private static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(10);

    private ServiceProvider _provider = null!;

    private IHostedService _sut = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantServices();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = wireMock.Url,
        });
        services.AddAssistantListener();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetServices<IHostedService>().Single();

        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    /// <summary>
    /// When the owner sends a message
    /// And the listener is running
    /// Then a reply comes back carrying what they sent.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessage_RepliesWithTheirText()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal("call the bank", sent[0].Text);
    }

    /// <summary>
    /// When someone other than the owner sends a message
    /// And the owner sends one in the same batch
    /// Then only the owner is answered.
    /// </summary>
    /// <remarks>
    /// The owner's message is a synchronisation device, not a second assertion. Proving
    /// that nothing was sent to the stranger otherwise means waiting on a clock and
    /// hoping; putting the stranger first in the batch means that by the time the owner's
    /// reply arrives, the stranger's message has already been processed and skipped.
    /// </remarks>
    [Fact]
    public async Task Listener_StrangerSendsAMessage_OnlyTheOwnerIsAnswered()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(
            new InboundUpdate(10, StrangerChatId, "let me in"),
            new InboundUpdate(11, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal("call the bank", Assert.Single(sent).Text);
    }
}
