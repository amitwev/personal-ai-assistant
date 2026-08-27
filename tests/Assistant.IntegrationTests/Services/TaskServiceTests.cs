using Assistant.Contracts;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;

namespace Assistant.IntegrationTests.Services;

/// <summary>
/// Test class for <see cref="ITaskService"/>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class TaskServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int NoLimit = 100;

    private static readonly DateTimeOffset AsOf =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider = postgres.CreateProvider();

    private ITaskService _sut = null!;

    private ITaskRepository _repository = null!;

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        _sut = _provider.GetRequiredService<ITaskService>();
        _repository = _provider.GetRequiredService<ITaskRepository>();
        return postgres.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a due task's reminder has been delivered
    /// And it is recorded as sent
    /// Then that task stops being due
    /// And every other due task remains due.
    /// </summary>
    [Fact]
    public async Task MarkReminderSentAsync_TaskWasDue_StopsBeingDue()
    {
        // Arrange
        var delivered = BuildReminderTask(dueAt: AsOf.AddHours(-2));
        var untouched = BuildReminderTask(dueAt: AsOf.AddHours(-1));
        await postgres.SaveAsync(delivered);
        await postgres.SaveAsync(untouched);

        // Act
        var result = await _sut.MarkReminderSentAsync(delivered.Id, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var stillDue = await _repository.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);
        Assert.Equal(untouched.Id, Assert.Single(stillDue).Id);
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
        await postgres.SaveAsync(reminderTask);

        // Act
        var result = await _sut.MarkReminderSentAsync(reminderTask.Id, CancellationToken.None);

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
        var result = await _sut.MarkReminderSentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TaskNotFound, result.Error);
    }
}
