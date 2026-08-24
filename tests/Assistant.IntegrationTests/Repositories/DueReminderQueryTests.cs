using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Repositories;

/// <summary>
/// Test class for <see cref="ITaskRepository.GetDueRemindersAsync"/>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <remarks>
/// Boundaries are probed at one microsecond, not one tick. Postgres <c>timestamptz</c> stores
/// microseconds and truncates below that, so a one-tick difference is not a difference at all
/// once the row is written.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DueReminderQueryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int TicksPerMicrosecond = 10;
    private const int NoLimit = 100;

    private static readonly DateTimeOffset AsOf =
        new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DueAnHourAgo = AsOf.AddHours(-1);

    private readonly ServiceProvider _provider = postgres.CreateProvider();

    private ITaskRepository Sut => _provider.GetRequiredService<ITaskRepository>();

    /// <inheritdoc/>
    public Task InitializeAsync() => postgres.ResetAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a pending task is due at or before the current instant
    /// And due reminders are requested as of that instant
    /// Then it is returned in full.
    /// </summary>
    [Theory]
    [InlineData(-TicksPerMicrosecond)]
    [InlineData(0)]
    public async Task GetDueRemindersAsync_DueAtAtOrBeforeNow_ReturnsTask(int ticksFromAsOf)
    {
        // Arrange
        var expected = BuildReminderTask(dueAt: AsOf.AddTicks(ticksFromAsOf));
        await SaveAsync(expected);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Equivalent(new[] { expected }, result, strict: true);
    }

    /// <summary>
    /// When a pending task is not due until after the current instant
    /// And due reminders are requested as of that instant
    /// Then it is not returned.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_DueAfterNow_ReturnsNothing()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: AsOf.AddTicks(TicksPerMicrosecond));
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When a due task is no longer pending
    /// And due reminders are requested
    /// Then it is not returned.
    /// </summary>
    [Theory]
    [InlineData(ReminderStatus.Completed)]
    [InlineData(ReminderStatus.Cancelled)]
    public async Task GetDueRemindersAsync_TaskNotPending_ReturnsNothing(ReminderStatus status)
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: DueAnHourAgo, status: status);
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When a pending task has no due time
    /// And due reminders are requested
    /// Then it is not returned.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_TaskHasNoDueTime_ReturnsNothing()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: null);
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When a due task has already had its reminder delivered
    /// And due reminders are requested
    /// Then it is not returned.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_ReminderAlreadySent_ReturnsNothing()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: DueAnHourAgo, reminderSentAt: AsOf);
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When several tasks are due
    /// And due reminders are requested
    /// Then they are returned oldest due time first.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_SeveralDue_ReturnsOldestFirst()
    {
        // Arrange
        var oldest = BuildReminderTask(dueAt: AsOf.AddHours(-3));
        var middle = BuildReminderTask(dueAt: AsOf.AddHours(-2));
        var newest = BuildReminderTask(dueAt: AsOf.AddHours(-1));

        await SaveAsync(middle);
        await SaveAsync(newest);
        await SaveAsync(oldest);

        var expected = new[] { oldest, middle, newest };

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        // Equivalent proves content and cardinality; it is blind to order, so Equal pins the sequence.
        Assert.Equivalent(expected, result, strict: true);
        Assert.Equal(expected.Select(task => task.Id), result.Select(task => task.Id));
    }

    /// <summary>
    /// When more tasks are due than the limit allows
    /// And due reminders are requested
    /// Then the oldest are returned, up to the limit.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_MoreDueThanLimit_ReturnsOldestWithinLimit()
    {
        // Arrange
        var oldest = BuildReminderTask(dueAt: AsOf.AddHours(-3));
        var middle = BuildReminderTask(dueAt: AsOf.AddHours(-2));
        var newest = BuildReminderTask(dueAt: AsOf.AddHours(-1));

        await SaveAsync(newest);
        await SaveAsync(oldest);
        await SaveAsync(middle);

        var expected = new[] { oldest, middle };

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, 2, CancellationToken.None);

        // Assert
        // Equivalent proves content and cardinality; it is blind to order, so Equal pins the sequence.
        Assert.Equivalent(expected, result, strict: true);
        Assert.Equal(expected.Select(task => task.Id), result.Select(task => task.Id));
    }

    private static ReminderTask BuildReminderTask(
        DateTimeOffset? dueAt,
        ReminderStatus status = ReminderStatus.Pending,
        DateTimeOffset? reminderSentAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "call the bank",
        Status = status,
        DueAt = dueAt,
        ReminderSentAt = reminderSentAt,
        CreatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
    };

    /// <summary>
    /// Saves a task through a provider of its own, then disposes it.
    /// </summary>
    /// <param name="reminderTask">The task to save.</param>
    /// <returns>A task that completes once the row has been written.</returns>
    private async Task SaveAsync(ReminderTask reminderTask)
    {
        await using var writer = postgres.CreateProvider();
        await writer.GetRequiredService<ITaskRepository>()
            .AddAsync(reminderTask, CancellationToken.None);
    }
}
