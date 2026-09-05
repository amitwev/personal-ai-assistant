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
[Collection(IntegrationCollection.Name)]
public sealed class TelegramNotifierTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;

    private ServiceProvider _provider = null!;

    private INotifier _sut = null!;

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
        _sut = _provider.GetRequiredService<INotifier>();
        return wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a message is sent
    /// And its text is either plain or contains characters MarkdownV2 would treat as formatting
    /// Then exactly one request reaches Telegram
    /// And it is addressed to the owner
    /// And the text is unchanged.
    /// </summary>
    [Theory]
    [InlineData("Call the bank")]
    [InlineData("Call the bank_now *urgent*")]
    public async Task SendAsync_Text_PostsOneMessageToTheOwner(string text)
    {
        // Arrange
        var expected = new SendMessagePayload(OwnerChatId, text, "Html");

        // Act
        await _sut.SendAsync(text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(await wireMock.SentMessagesAsync()), strict: true);
    }

    /// <summary>
    /// When a message contains "&amp;", "&lt;" and "&gt;"
    /// And it is sent to the owner
    /// Then all three are escaped on the wire and nothing else changes.
    /// </summary>
    /// <remarks>
    /// The expected string below was worked out by hand, not produced by running the same
    /// <c>Replace</c> chain the production code uses — asserting against an identically derived
    /// value would prove nothing about whether the escaping is actually correct. The text is
    /// shaped so a naive wrong-order implementation would visibly differ: escaping "&lt;" and
    /// "&gt;" before "&amp;" would re-escape the ampersand those replacements introduce, turning
    /// "&lt;" into the literal text "&amp;lt;" on the wire instead of a rendered angle bracket.
    /// </remarks>
    [Fact]
    public async Task SendAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder()
    {
        // Arrange
        const string text = "Meet R&D <at 5> & confirm";
        var expected = new SendMessagePayload(OwnerChatId, "Meet R&amp;D &lt;at 5&gt; &amp; confirm", "Html");

        // Act
        await _sut.SendAsync(text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(await wireMock.SentMessagesAsync()), strict: true);
    }

    /// <summary>
    /// When a message contains only non-ASCII text
    /// And it is sent to the owner
    /// Then it reaches the wire unchanged, byte for byte.
    /// </summary>
    /// <remarks>
    /// Regression test for delegating escaping to a general-purpose HTML encoder instead of
    /// hand-rolling the three replacements the wire format actually needs. Verified directly on
    /// .NET 10: <see cref="System.Net.WebUtility.HtmlEncode(string)"/> happens to leave this exact
    /// Hebrew string alone — its non-ASCII handling only reaches the Latin-1 Supplement range and
    /// characters outside the Basic Multilingual Plane, so "café" becomes "caf&amp;#233;" but
    /// Hebrew passes through untouched — while <c>System.Text.Encodings.Web.HtmlEncoder.Default</c>,
    /// an equally reachable choice for the same job, numeric-encodes every character in this
    /// string. Either one is a trap a maintainer could reach for without noticing; the hand-rolled
    /// <c>Escape</c> cannot fall into it because it only ever touches "&amp;", "&lt;" and "&gt;".
    /// </remarks>
    [Fact]
    public async Task SendAsync_HebrewText_PassesThroughByteForByte()
    {
        // Arrange
        const string text = "שלום, נא להתקשר לבנק";
        var expected = new SendMessagePayload(OwnerChatId, text, "Html");

        // Act
        await _sut.SendAsync(text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(await wireMock.SentMessagesAsync()), strict: true);
    }

    /// <summary>
    /// When a previously sent message is marked complete
    /// And its title contains "&amp;", "&lt;" and "&gt;"
    /// Then the edit escapes all three in order inside the struck-through wrapper.
    /// </summary>
    /// <remarks>
    /// The expected string below was worked out by hand, the same discipline
    /// <see cref="SendAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder"/> already
    /// applies. The expected <see cref="ReplyMarkupPayload"/> is asserted here only because
    /// <c>strict: true</c> compares the whole payload -- this test's own subject is escaping, not
    /// the empty keyboard, which <c>CallbackRouterTests</c> already proves through a real tap.
    /// </remarks>
    [Fact]
    public async Task MarkCompletedTaskAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder()
    {
        // Arrange
        const int messageId = 42;
        const string text = "Meet R&D <at 5> & confirm";
        var expected = new EditMessageTextPayload(
            OwnerChatId, messageId, "<s>Meet R&amp;D &lt;at 5&gt; &amp; confirm</s>", "Html",
            new ReplyMarkupPayload([]));

        // Act
        await _sut.MarkCompletedTaskAsync(messageId, text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(await wireMock.EditedMessagesAsync()), strict: true);
    }
}
