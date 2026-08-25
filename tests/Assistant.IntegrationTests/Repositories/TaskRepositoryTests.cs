using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;

namespace Assistant.IntegrationTests.Repositories;

/// <summary>
/// Test class for <see cref="ITaskRepository"/>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <remarks>
/// Every instant here is a literal with at most six fractional digits. Postgres
/// <c>timestamptz</c> holds microseconds while .NET ticks are 100ns, so a value taken from
/// <c>DateTimeOffset.UtcNow</c> can be truncated on read. Whether it is depends on the host
/// clock's resolution, which is what would make such a test pass on one machine and fail on
/// another.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class TaskRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset DueAt = new(2026, 8, 22, 10, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider = postgres.CreateProvider();

    private ITaskRepository _sut = null!;

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        _sut = _provider.GetRequiredService<ITaskRepository>();
        return postgres.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a task has been saved
    /// And it is looked up by its identifier
    /// Then every property holds the value it was saved with.
    /// </summary>
    [Fact]
    public async Task FindAsync_TaskWasAdded_ReturnsTaskUnchanged()
    {
        // Arrange
        var expected = BuildReminderTask(dueAt: DueAt);
        await postgres.SaveAsync(expected);

        // Act
        var result = await _sut.FindAsync(expected.Id, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, result, strict: true);
    }

    /// <summary>
    /// When a task has been saved
    /// And it is looked up by its identifier
    /// Then its instants are returned in UTC.
    /// </summary>
    [Fact]
    public async Task FindAsync_TaskWasAdded_ReturnsInstantsInUtc()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: DueAt);
        await postgres.SaveAsync(reminderTask);

        // Act
        var result = await _sut.FindAsync(reminderTask.Id, CancellationToken.None);

        // Assert
        Assert.Equal(TimeSpan.Zero, result!.DueAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, result.CreatedAt.Offset);
    }

    /// <summary>
    /// When no task carries the requested identifier
    /// And it is looked up by that identifier
    /// Then nothing is returned.
    /// </summary>
    [Fact]
    public async Task FindAsync_NoRowWithThatId_ReturnsNull()
    {
        // Act
        var result = await _sut.FindAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// When a task is saved with no status set
    /// Then it is rejected.
    /// </summary>
    [Fact]
    public async Task AddAsync_StatusUnknown_IsRejected()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: DueAt, status: ReminderStatus.Unknown);

        // Act
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => _sut.AddAsync(reminderTask, CancellationToken.None));

        // Assert
        // ConstraintName is what proves the database refused the row rather than EF or the application.
        Assert.Equal(
            "ck_reminder_tasks_status_known",
            FindPostgresException(exception)!.ConstraintName);
    }

    /// <summary>
    /// Walks an exception chain to the provider exception underneath it.
    /// </summary>
    /// <param name="exception">The exception the repository threw.</param>
    /// <returns>The innermost <see cref="PostgresException"/>, or null if there is none.</returns>
    /// <remarks>
    /// Entity Framework wraps provider exceptions, but this project cannot name the wrapper: the
    /// EF packages are marked <c>PrivateAssets="compile"</c> in Assistant.Repository, so they do
    /// not flow here at compile time. Asserting on the Npgsql exception is the stronger test
    /// anyway, because it does not depend on how EF chooses to wrap.
    /// </remarks>
    private static PostgresException? FindPostgresException(Exception? exception)
    {
        while (exception is not null and not PostgresException)
        {
            exception = exception.InnerException;
        }

        return exception as PostgresException;
    }
}
