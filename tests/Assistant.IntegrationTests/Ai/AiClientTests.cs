using System.Text.Json;
using Assistant.Contracts;
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
        services.AddLogging();
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
    /// When the provider calls a tool
    /// And the model is asked
    /// Then the tool's name and raw arguments come back as the result's value.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderCallsATool_ReturnsItsNameAndArguments()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync("create_task", """{"title":"call the bank"}""");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("create_task", result.Value!.Name);
        Assert.Equal("""{"title":"call the bank"}""", result.Value.ArgumentsJson);
    }

    /// <summary>
    /// When the provider calls create_task with a title and a due time
    /// And the arguments are parsed as a CreateTaskRequest
    /// Then both fields come back exactly as the model sent them.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderCallsCreateTask_ArgumentsParseAsACreateTaskRequest()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync(
            "create_task", """{"title":"call the bank","due_at_local":"2026-09-01T10:00:00"}""");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        var request = JsonSerializer.Deserialize<CreateTaskRequest>(result.Value!.ArgumentsJson);
        Assert.Equal("call the bank", request!.Title);
        Assert.Equal("2026-09-01T10:00:00", request.DueAtLocal);
    }

    /// <summary>
    /// When the model is asked
    /// Then the system prompt is sent as the first message with role system
    /// And the owner's text is sent as the second message with role user
    /// And the configured model, token limit and tool definition go on the wire.
    /// </summary>
    [Fact]
    public async Task AskAsync_AnyText_PlacesThePromptTheModelAndTheToolOnTheWire()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync("create_task", """{"title":"call the bank"}""");

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
        var tool = Assert.Single(request.Tools);
        Assert.Equal("create_task", tool.Function.Name);
    }

    /// <summary>
    /// When the provider answers with a server error
    /// And the model is asked
    /// Then the call is refused as unavailable, not thrown.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderReturnsAServerError_IsRefusedAsUnavailable()
    {
        // Arrange
        await wireMock.SeedAiFailureAsync();

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelUnavailable, result.Error);
    }

    /// <summary>
    /// When the provider answers with no candidate messages
    /// And the model is asked
    /// Then the call is refused as having returned nothing.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderReturnsNoChoices_IsRefusedAsNoAnswer()
    {
        // Arrange
        await wireMock.SeedAiNoAnswerAsync();

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelReturnedNoAnswer, result.Error);
    }

    /// <summary>
    /// When the provider answers with prose instead of calling a tool
    /// And the model is asked
    /// Then the call is refused as having named no tool, not thrown.
    /// </summary>
    [Fact]
    public async Task AskAsync_ProviderRepliesWithProse_IsRefusedAsNoToolCall()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Sure, I can help with that.");

        // Act
        var result = await _sut.AskAsync("call the bank tomorrow at 10", CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ModelReturnedNoToolCall, result.Error);
    }
}
