using Assistant.IntegrationTests.Infrastructure;
using Npgsql;

namespace Assistant.IntegrationTests.Schema;

/// <summary>
/// Test class for the <c>reminder_tasks</c> schema.
/// </summary>
/// <remarks>
/// The tests here fabricate a row with raw SQL that no application code can currently produce,
/// each proving that one check constraint in ReminderTaskConfiguration exists and carries the
/// predicate we intended: that predicate is hand-written SQL in a C# string literal, so neither
/// the compiler nor EF validates it.
/// <para>
/// To decide whether a given one has become redundant, drop that constraint in the database
/// (not the configuration) with raw SQL and run the suite. If a test outside this class fails,
/// the constraint is covered elsewhere and this test can go. If only this test fails, it is
/// still the sole guard, so keep it. F2 applied this check to the status constraint: dropping
/// ck_reminder_tasks_status_known failed both this class's raw-SQL test and
/// TaskRepositoryTests.AddAsync_StatusUnknown_IsRejected, so the raw-SQL test for that
/// constraint was retired in favor of the application-level one. F5a applied it to
/// ck_reminder_tasks_sent_requires_due and kept its raw-SQL test as the sole guard, per
/// Insert_ReminderSentWithoutDueTime_IsRejected's own remarks. F6-1 applies it to
/// ck_reminder_tasks_completed_consistency; see
/// Insert_CompletedWithoutCompletedAt_IsRejected's own remarks for the result.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class ReminderTaskSchemaTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <inheritdoc/>
    public Task InitializeAsync() => postgres.ResetAsync();

    /// <inheritdoc/>
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// When a row is inserted with reminder_sent_at set and due_at NULL
    /// And the insert is attempted directly with raw SQL
    /// Then it is rejected.
    /// </summary>
    /// <remarks>
    /// The retirement check described in the class remarks was run at F5a: with
    /// <c>ck_reminder_tasks_sent_requires_due</c> dropped from the live database, the suite
    /// reported 1 failed / 18 passed, and this test was the sole failure. F5a's
    /// <c>MarkReminderSentAsync_TaskHasNoDueTime_IsRejected</c> asserts
    /// <c>ErrorCode.DueTimeMissing</c>, which is the application refusing before the row ever
    /// reaches Postgres, so it does not depend on the database rejecting anything and cannot
    /// replace this test. This remains the only guard for the constraint.
    /// </remarks>
    [Fact]
    public async Task Insert_ReminderSentWithoutDueTime_IsRejected()
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
        // ConstraintName is what proves the database refused the row rather than EF or the application.
        Assert.Equal("ck_reminder_tasks_sent_requires_due", ex.ConstraintName);
    }

    /// <summary>
    /// When a row is inserted with status Completed and completed_at NULL
    /// And the insert is attempted directly with raw SQL
    /// Then it is rejected.
    /// </summary>
    /// <remarks>
    /// The retirement check described in the class remarks was run at F6-1: with
    /// <c>ck_reminder_tasks_completed_consistency</c> dropped from the live database, the suite
    /// reported 1 failed / 35 passed, and this test was the sole failure. This remains
    /// the only guard for the constraint.
    /// </remarks>
    [Fact]
    public async Task Insert_CompletedWithoutCompletedAt_IsRejected()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reminder_tasks (id, title, status, created_at, updated_at)
            VALUES (gen_random_uuid(), 'bad', 2, now(), now())
            """;

        // Act
        var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        // Assert
        // ConstraintName is what proves the database refused the row rather than EF or the application.
        Assert.Equal("ck_reminder_tasks_completed_consistency", ex.ConstraintName);
    }
}
