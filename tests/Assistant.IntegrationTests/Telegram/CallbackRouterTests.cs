using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for the callback-query handler registered via <c>AddAssistantListener</c>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class CallbackRouterTests(PostgresFixture postgres, WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;
    private const long StrangerChatId = 999888777L;
    private const int MessageId = 55;
    private const string CallbackQueryId = "cb-1";

    private static readonly TimeSpan AnswerDeadline = TimeSpan.FromSeconds(10);

    private ServiceProvider _provider = null!;

    private IHostedService _sut = null!;

    private ITaskRepository _repository = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = wireMock.Url,
        });
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantAi(new AiSettings
        {
            ApiKey = "test-key", BaseUrl = wireMock.Url, Model = "test-model", MaxTokens = 100,
        });
        services.AddAssistantListener();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetServices<IHostedService>().Single();
        _repository = _provider.GetRequiredService<ITaskRepository>();

        await postgres.ResetAsync();
        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    /// <summary>
    /// When the owner taps Done on a pending task
    /// Then the task is completed
    /// And the reminder message is edited to show it struck through
    /// And the callback query is answered.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerTapsDone_CompletesTheTaskAndStrikesThroughTheMessage()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode("done", task.Id);
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var expectedAnswer = new AnswerCallbackQueryPayload(CallbackQueryId, null);
        Assert.Equivalent(expectedAnswer, Assert.Single(answered), strict: true);

        var expectedEdit = new EditMessageTextPayload(OwnerChatId, MessageId, $"<s>{task.Title}</s>", "Html");
        Assert.Equivalent(expectedEdit, Assert.Single(await wireMock.EditedMessagesAsync()), strict: true);

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(ReminderStatus.Completed, stored!.Status);
        Assert.NotNull(stored.CompletedAt);
    }

    /// <summary>
    /// When the owner taps Done on a task that is already completed
    /// Then the callback query is answered that it is already done
    /// And the message is not edited again
    /// And the stored completion instant is unchanged.
    /// </summary>
    [Fact]
    public async Task Listener_DoneTappedOnAnAlreadyCompletedTask_AnswersAlreadyDoneWithoutEditingAgain()
    {
        // Arrange
        var originalCompletedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var task = BuildReminderTask(status: ReminderStatus.Completed, completedAt: originalCompletedAt);
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode("done", task.Id);
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var expectedAnswer = new AnswerCallbackQueryPayload(CallbackQueryId, "Already done.");
        Assert.Equivalent(expectedAnswer, Assert.Single(answered), strict: true);

        Assert.Empty(await wireMock.EditedMessagesAsync());

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(originalCompletedAt, stored!.CompletedAt);
    }

    /// <summary>
    /// When the callback data is malformed or names an action nothing implements
    /// Then the callback query is still answered
    /// And nothing is edited.
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("v1:archive:AAAAAAAAAAAAAAAAAAAAAA==")]
    public async Task Listener_UnrecognisedCallbackData_StillAnswersButEditsNothing(string data)
    {
        // Arrange
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, "call the bank", data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        Assert.Equal(CallbackQueryId, Assert.Single(answered).CallbackQueryId);
        Assert.Empty(await wireMock.EditedMessagesAsync());
    }

    /// <summary>
    /// When someone other than the owner taps a button
    /// Then the callback query is still answered
    /// And the task is left untouched.
    /// </summary>
    [Fact]
    public async Task Listener_StrangerTapsTheButton_AnswersButLeavesTheTaskUntouched()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode("done", task.Id);
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, StrangerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        Assert.Equal(CallbackQueryId, Assert.Single(answered).CallbackQueryId);

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(ReminderStatus.Pending, stored!.Status);
    }
}
