using Assistant.Impl;
using Assistant.Impl.Settings;
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

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = wireMock.Url,
        });
        services.AddAssistantScheduler();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IScheduledJob>();

        await postgres.ResetAsync();
        await wireMock.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a task is due
    /// And the job runs
    /// Then exactly one message is sent, carrying the task's title behind the ⏰ prefix.
    /// </summary>
    [Fact]
    public async Task RunAsync_TaskIsDue_SendsItsTitleBehindThePrefix()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
        await postgres.SaveAsync(task);

        // Act
        await _sut.RunAsync(CancellationToken.None);

        // Assert
        var sent = Assert.Single(await wireMock.SentMessagesAsync());
        Assert.Equal($"⏰ {task.Title}", sent.Text);
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
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
        services.AddAssistantTelegram(new TelegramSettings
        {
            BotToken = BotToken, OwnerChatId = OwnerChatId, BaseUrl = UnreachableBaseUrl,
        });
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
