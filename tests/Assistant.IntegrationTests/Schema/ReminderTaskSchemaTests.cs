using Assistant.IntegrationTests.Infrastructure;
using Npgsql;

namespace Assistant.IntegrationTests.Schema;

/// <summary>
/// Test class for the <c>reminder_tasks</c> schema.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReminderTaskSchemaTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;

    public ReminderTaskSchemaTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// When a row is inserted with status 0 (Unknown)
    /// And the insert is attempted directly with raw SQL
    /// Then it throws, naming ck_reminder_tasks_status_known as the violated constraint.
    /// </summary>
    [Fact]
    public async Task Insert_StatusUnknown_ViolatesStatusKnownConstraint()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reminder_tasks (id, title, status, created_at, updated_at)
            VALUES (gen_random_uuid(), 'bad', 0, now(), now())
            """;

        // Act
        var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal("ck_reminder_tasks_status_known", ex.ConstraintName);
    }

    /// <summary>
    /// When a row is inserted with reminder_sent_at set and due_at NULL
    /// And the insert is attempted directly with raw SQL
    /// Then it throws, naming ck_reminder_tasks_sent_requires_due as the violated constraint.
    /// </summary>
    [Fact]
    public async Task Insert_ReminderSentWithoutDueTime_ViolatesSentRequiresDueConstraint()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reminder_tasks (id, title, status, reminder_sent_at, created_at, updated_at)
            VALUES (gen_random_uuid(), 'bad', 1, now(), now(), now())
            """;

        // Act
        var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal("ck_reminder_tasks_sent_requires_due", ex.ConstraintName);
    }
}
