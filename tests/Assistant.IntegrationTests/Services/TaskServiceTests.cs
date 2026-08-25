using Assistant.Contracts;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Services;

/// <summary>
/// Test class for <see cref="ITaskService"/>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
[Collection(PostgresCollection.Name)]
public sealed class TaskServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int NoLimit = 100;

    private static readonly DateTimeOffset AsOf =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider = postgres.CreateProvider();

    private ITaskService Sut => _provider.GetRequiredService<ITaskService>();

    private ITaskRepository Repository => _provider.GetRequiredService<ITaskRepository>();

    /// <inheritdoc/>
    public Task InitializeAsync() => postgres.ResetAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a due task's reminder has been delivered
    /// And it is recorded as sent
    /// Then the task is no longer due, so the reminder is never delivered twice.
    /// </summary>
    [Fact]
    public async Task MarkReminderSentAsync_TaskWasDue_StopsBeingDue()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: AsOf.AddHours(-1));
        await postgres.SaveAsync(reminderTask);

        // Act
        var result = await Sut.MarkReminderSentAsync(reminderTask.Id, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(await Repository.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None));
    }

    /// <summary>
    /// When a task has no due time
    /// And its reminder is recorded as sent
    /// Then it is refused, because there was no reminder to deliver.
    /// </summary>
    [Fact]
    public async Task MarkReminderSentAsync_TaskHasNoDueTime_IsRejected()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: null);
        await Repository.AddAsync(reminderTask, CancellationToken.None);

        // Act
        var result = await Sut.MarkReminderSentAsync(reminderTask.Id, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.DueTimeMissing, result.Error);
    }

    /// <summary>
    /// When no task carries the requested identifier
    /// And its reminder is recorded as sent
    /// Then it is refused rather than silently doing nothing.
    /// </summary>
    [Fact]
    public async Task MarkReminderSentAsync_TaskDoesNotExist_IsRejected()
    {
        // Act
        var result = await Sut.MarkReminderSentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TaskNotFound, result.Error);
    }

    private static ReminderTask BuildReminderTask(DateTimeOffset? dueAt) => new()
    {
        Id = Guid.NewGuid(),
        Title = "call the bank",
        Status = ReminderStatus.Pending,
        DueAt = dueAt,
        ReminderSentAt = null,
        CreatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
    };
}
