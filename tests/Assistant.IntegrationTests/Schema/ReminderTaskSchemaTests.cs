using Assistant.IntegrationTests.Infrastructure;
using Npgsql;

namespace Assistant.IntegrationTests.Schema;

/// <summary>
/// Test class for the <c>reminder_tasks</c> schema.
/// </summary>
/// <remarks>
/// The remaining test here fabricates a row with raw SQL that no application code can
/// currently produce, because nothing writes reminder_sent_at without due_at yet. It is the
/// only proof that the check constraint in ReminderTaskConfiguration exists and carries the
/// predicate we intended: that predicate is hand-written SQL in a C# string literal, so
/// neither the compiler nor EF validates it.
/// <para>
/// To decide whether it has become redundant, drop the constraint in the database (not the
/// configuration) with raw SQL and run the suite. If a test outside this class fails, the
/// constraint is covered elsewhere and this test can go. If only this test fails, it is still
/// the sole guard, so keep it. F2 applied this check to the status constraint: dropping
/// ck_reminder_tasks_status_known failed both this class's raw-SQL test and
/// TaskRepositoryTests.AddAsync_StatusUnknown_RejectedByStatusConstraint, so the raw-SQL test
/// for that constraint was retired in favor of the application-level one.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ReminderTaskSchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <inheritdoc/>
    public Task InitializeAsync() => postgres.ResetAsync();

    /// <inheritdoc/>
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// When a row is inserted with reminder_sent_at set and due_at NULL
    /// And the insert is attempted directly with raw SQL
    /// Then it throws, naming ck_reminder_tasks_sent_requires_due as the violated constraint.
    /// </summary>
    /// <remarks>
    /// Retirement candidate at F5, which adds an application-level test through
    /// MarkReminderSentAsync. F5's test has to call the service directly: the scheduler cannot
    /// reach this state, because GetDueRemindersAsync filters on due_at and so never returns a
    /// task without one. As above, F5 only replaces this test if its assertion depends on the
    /// database rejecting the row. Apply the check in the class remarks.
    /// </remarks>
    [Fact]
    public async Task Insert_ReminderSentWithoutDueTime_ViolatesSentRequiresDueConstraint()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
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
