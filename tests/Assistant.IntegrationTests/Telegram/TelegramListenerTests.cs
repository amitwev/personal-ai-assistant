using Assistant.Contracts;
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.IntegrationTests.Telegram;

/// <summary>
/// Test class for the inbound listener registered via <c>AddAssistantListener</c>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class TelegramListenerTests(PostgresFixture postgres, WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;
    private const long StrangerChatId = 999888777L;
    private const int NoLimit = 100;

    private const string NotUnderstoodAsATask =
        "I did not read that as a task. Try rephrasing it.";

    private const string DueTimeInPastReply =
        "That time has already passed. What time did you mean?";

    private const string DueTimeTooFarAheadReply =
        "That is more than two years away, which is probably not what you meant. "
        + "What time did you mean?";

    private const string DueTimeUnparseableReply =
        "I could not make sense of that time. What time did you mean?";

    private const string TitleMissingReply =
        "I did not catch what to call that. What should I call it?";

    private const string SomethingWentWrongReply =
        "Something went wrong on my end. Send that again in a moment.";

    private static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(10);

    private static readonly DateTimeOffset AsOf = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

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
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));
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
        await wireMock.SeedAiToolCallAsync(
            "create_task", """{"title":"call the bank","due_at_local":"2026-08-26T10:00:00"}""");
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    /// <summary>
    /// When the owner sends a message
    /// And the model calls create_task with a due time that resolves
    /// Then the task is stored with that due instant
    /// And the owner is told the title and the due time, rendered in the configured zone
    /// And the reply carries a Done button for that exact task.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton()
    {
        // Arrange
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank tomorrow at 10"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal("call the bank -- due Wednesday 26 August 2026, 10:00.", sent[0].Text);

        var stored = Assert.Single(
            await _repository.GetDueRemindersAsync(AsOf.AddYears(10), NoLimit, CancellationToken.None));
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 7, 0, 0, TimeSpan.Zero), stored.DueAt);

        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
        var button = Assert.Single(row);
        Assert.Equal(TaskActions.Done.Label, button.Text);
        Assert.Equal(CallbackCodec.Encode(TaskActions.Done.Key, stored.Id), button.CallbackData);
    }

    /// <summary>
    /// When the owner sends a message
    /// And the model calls create_task with no due time
    /// Then the owner is told plainly that no reminder will fire
    /// And the reply still carries a Done button.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerSendsAMessageWithNoDueTime_RepliesThatNoReminderWillFire()
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync("create_task", """{"title":"Buy milk"}""");
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "buy milk"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal("Buy milk -- saved with no reminder.", sent[0].Text);

        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
        var button = Assert.Single(row);
        Assert.Equal(TaskActions.Done.Label, button.Text);
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
    /// <para>
    /// This test does not check the reply's exact text: that check duplicated
    /// <see cref="Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton"/>,
    /// which spec §7.2 forbids. What this test alone proves is that the stranger's message
    /// produced no second reply.
    /// </para>
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
        Assert.Single(sent);
    }

    /// <summary>
    /// When a message has been answered
    /// And the listener keeps polling
    /// Then it is not answered again.
    /// </summary>
    /// <remarks>
    /// The only test in this suite that waits on wall-clock time, and it is worth the
    /// cost: a listener that fails to advance its offset is served the same update on
    /// every poll, and the stub answers an unadvanced poll with no delay at all.
    /// </remarks>
    [Fact]
    public async Task Listener_MessageAlreadyAnswered_DoesNotAnswerItAgain()
    {
        // Arrange
        var settle = TimeSpan.FromSeconds(3);
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        await Task.Delay(settle);

        // Assert
        Assert.Single(await wireMock.SentMessagesAsync());
    }

    /// <summary>
    /// When the model replies with prose instead of calling a tool
    /// And the owner sent the message that produced it
    /// Then the owner is told the message was not read as a task.
    /// </summary>
    [Fact]
    public async Task Listener_ModelRepliesWithProse_TellsTheOwnerItWasNotReadAsATask()
    {
        // Arrange
        await wireMock.SeedAiAnswerAsync("Sure, tell me more.");
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "hello"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal(NotUnderstoodAsATask, sent[0].Text);
    }

    /// <summary>
    /// When the owner sends a message
    /// And the model's tool call cannot be carried out
    /// Then the owner is told a plain sentence with no button
    /// And nothing else is sent.
    /// </summary>
    /// <param name="toolName">The tool name the stubbed tool call carries.</param>
    /// <param name="argumentsJson">The arguments JSON the stubbed tool call carries.</param>
    /// <param name="expectedReply">The sentence the owner should see.</param>
    /// <remarks>
    /// Whether a row was written for each of these is proven once, at the tool level, by
    /// <c>CreateTaskToolTests</c> -- repeating that proof here through a real Telegram round
    /// trip would duplicate coverage spec §7.2 forbids. The unregistered-tool-name row has no
    /// such proof anywhere else, and needs none: <c>MessageHandler</c> only calls an
    /// <see cref="IAssistantTool"/> once one has actually been found, so there is no code path
    /// from an unmatched name to a persisted row to test in the first place.
    /// </remarks>
    [Theory]
    [InlineData("create_task", """{"title":"Call the bank","due_at_local":"2026-08-25T10:00:00"}""", DueTimeInPastReply)]
    [InlineData("create_task", """{"title":"Call the bank","due_at_local":"2029-06-01T00:00:00"}""", DueTimeTooFarAheadReply)]
    [InlineData("create_task", """{"title":"Call the bank","due_at_local":"not a date"}""", DueTimeUnparseableReply)]
    [InlineData("create_task", """{"due_at_local":"2026-08-26T10:00:00"}""", TitleMissingReply)]
    [InlineData("create_task", "not json at all", SomethingWentWrongReply)]
    [InlineData("update_task", """{"anything":"here"}""", SomethingWentWrongReply)]
    public async Task Listener_ModelsToolCallCannotBeCarriedOut_RepliesWithNoButton(
        string toolName, string argumentsJson, string expectedReply)
    {
        // Arrange
        await wireMock.SeedAiToolCallAsync(toolName, argumentsJson);
        await wireMock.SeedUpdatesAsync(new InboundUpdate(10, OwnerChatId, "call the bank"));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.WaitForSentMessagesAsync(1, ReplyDeadline);
        Assert.Equal(expectedReply, sent[0].Text);
        Assert.Null(sent[0].ReplyMarkup);
    }
}
