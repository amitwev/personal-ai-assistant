using Assistant.Contracts;
using Assistant.Impl;
using Assistant.Impl.Mapping;
using Assistant.Impl.Settings;
using Assistant.Impl.Telegram;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;

namespace Assistant.IntegrationTests.Jobs;

/// <summary>
/// Test class for the due-reminder job registered via <c>AddAssistantScheduler</c>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <param name="wireMock">The shared stub API fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class DueReminderJobTests(PostgresFixture postgres, WireMockFixture wireMock) : IAsyncLifetime
{
    private const string BotToken = "123456:TESTTOKEN";
    private const long OwnerChatId = 100200300L;
    private const string UnreachableBaseUrl = "http://localhost:1";

    private ServiceProvider _provider = null!;

    private IScheduledJob _sut = null!;

    private ITaskService _taskService = null!;

    private INotifier _notifier = null!;

    private ILocalTimeResolver _clock = null!;

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
        services.AddAssistantScheduler();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IScheduledJob>();
        _taskService = _provider.GetRequiredService<ITaskService>();
        _notifier = _provider.GetRequiredService<INotifier>();
        _clock = _provider.GetRequiredService<ILocalTimeResolver>();

        await postgres.ResetAsync();
        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a task is due
    /// And the job runs
    /// Then exactly one message is sent, carrying its rendered due-time text.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskIsDue_SendsItsRenderedDueTimeText()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        var sent = Assert.Single(await wireMock.SentMessagesAsync());
        Assert.Equal(task.ToMessageText(_clock), sent.Text);
    }

    /// <summary>
    /// When a task was announced before its reminder fires
    /// And the reminder fires
    /// Then the chat holds one live message, not two.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskWasAnnouncedBeforeItFires_DeletesThePreviousMessage()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        await wireMock.SeedNextMessageIdAsync(100);
        var announcedMessageId = await _notifier.AnnounceTaskAsync(
            task.MessageId, task.Id, task.ToMessageText(_clock), CancellationToken.None);
        await _taskService.RecordMessageAsync(task.Id, announcedMessageId, CancellationToken.None);
        await wireMock.SeedNextMessageIdAsync(200);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, (await wireMock.SentMessagesAsync()).Count);
        var deleted = Assert.Single(await wireMock.DeletedMessagesAsync());
        Assert.Equal(100, deleted.MessageId);
    }

    /// <summary>
    /// When a task was announced before its reminder fires
    /// And the reminder fires
    /// Then the fired message shows the same due-time text the announcement showed.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskWasAnnouncedBeforeItFires_RendersTheSameTextAsTheAnnouncement()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        await wireMock.SeedNextMessageIdAsync(100);
        var announcedMessageId = await _notifier.AnnounceTaskAsync(
            task.MessageId, task.Id, task.ToMessageText(_clock), CancellationToken.None);
        await _taskService.RecordMessageAsync(task.Id, announcedMessageId, CancellationToken.None);
        await wireMock.SeedNextMessageIdAsync(200);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        var sent = await wireMock.SentMessagesAsync();
        Assert.Equal(2, sent.Count);
        Assert.Equal(task.ToMessageText(_clock), sent[0].Text);
        Assert.Equal(sent[0].Text, sent[1].Text);
    }

    /// <summary>
    /// When a task stored before this change fires
    /// And it carries no MessageId
    /// Then it is announced
    /// And nothing is deleted.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskHasNoStoredMessageId_AnnouncesItAndDeletesNothing()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Single(await wireMock.SentMessagesAsync());
        Assert.Empty(await wireMock.DeletedMessagesAsync());
    }

    /// <summary>
    /// When a task was announced before its reminder fires
    /// And its previous message cannot be deleted
    /// Then the reminder still arrives.
    /// </summary>
    [Fact]
    public async Task RunAsync_ThePreviousMessageCannotBeDeleted_TheReminderStillArrives()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);
        await wireMock.SeedNextMessageIdAsync(100);
        var announcedMessageId = await _notifier.AnnounceTaskAsync(
            task.MessageId, task.Id, task.ToMessageText(_clock), CancellationToken.None);
        await _taskService.RecordMessageAsync(task.Id, announcedMessageId, CancellationToken.None);
        await wireMock.SeedNextMessageIdAsync(200);
        await wireMock.SeedDeleteMessageFailureAsync();

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, (await wireMock.SentMessagesAsync()).Count);
    }

    /// <summary>
    /// When a pending task's due time has arrived
    /// And the job runs
    /// Then the message carries an inline keyboard with the Done and Schedule buttons for that task.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskIsDue_AttachesTheDoneAndScheduleButtonsForThatTask()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        var sent = Assert.Single(await wireMock.SentMessagesAsync());
        var expectedRow = new[]
        {
            new InlineButtonPayload(TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, task.Id)),
            new InlineButtonPayload(
                TaskNavigations.Schedule.Label, CallbackCodec.Encode(TaskNavigations.Schedule.Key, task.Id)),
        };
        Assert.Equivalent(expectedRow, Assert.Single(sent.ReplyMarkup!.InlineKeyboard), strict: true);
    }

    /// <summary>
    /// When a task's reminder has already been delivered
    /// And the job runs again
    /// Then no second message is sent.
    /// </summary>
    [Fact]
    public async Task RunAsync_ReminderAlreadySent_DoesNotSendASecondMessage()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Single(await wireMock.SentMessagesAsync());
    }

    /// <summary>
    /// When a task has been due for three days
    /// And the job runs
    /// Then its reminder is still delivered.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskHasBeenDueForThreeDays_StillDeliversIt()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddDays(-3));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.Single(await wireMock.SentMessagesAsync());
    }

    /// <summary>
    /// When a task is due
    /// And delivery fails
    /// Then the task is still due for the next run.
    /// </summary>
    [Fact]
    public async Task RunAsync_DeliveryFails_TaskIsStillDue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = UnreachableBaseUrl,
        });
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddAssistantScheduler();
        await using var provider = services.BuildServiceProvider();
        var sut = provider.GetRequiredService<IScheduledJob>();

        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await Assert.ThrowsAnyAsync<Exception>(() => sut.RunAsync(CancellationToken.None));

        // Assert
        var repository = _provider.GetRequiredService<ITaskRepository>();
        var stillDue = await repository.GetDueRemindersAsync(
            DateTimeOffset.UtcNow, 100, CancellationToken.None);
        Assert.Equal(task.Id, Assert.Single(stillDue).Id);
    }
}
