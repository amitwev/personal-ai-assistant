using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Ai;

/// <summary>
/// Test class for <see cref="IAiClient"/>.
/// </summary>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class AiClientTests(WireMockFixture wireMock) : IAsyncLifetime
{
    private const string Model = "test-model";
    private const int MaxTokens = 100;

    private ServiceProvider _provider = null!;

    private IAiClient _sut = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantServices();
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = Model, MaxTokens = MaxTokens,
        });
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IAiClient>();

        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When the provider answers with a candidate message
    /// And the model is asked
    /// Then its text comes back as the result's value.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderAnswers_ReturnsItsText()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Noted -- I will remind you.");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Noted -- I will remind you.", result.Value);
    }

    /// <summary>
    /// When the model is asked
    /// Then the system prompt is sent as the first message with role system
    /// And the owner's text is sent as the second message with role user
    /// And the configured model and token limit go on the wire.
    /// </summary>
    [Fact]
    public async Task AskAsync_AnyText_PlacesThePromptAndTheModelCorrectlyOnTheWire()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Noted.");

        // Act
        await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        var request = Assert.Single(await wireMock.AiRequestsAsync());
        Assert.Equal(Model, request.Model);
        Assert.Equal(MaxTokens, request.MaxTokens);
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal("system", request.Messages[0].Role);
        Assert.Equal("user", request.Messages[1].Role);
        Assert.Equal("call the bank tomorrow at 10", request.Messages[1].Content);
    }
}
