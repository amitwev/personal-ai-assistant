using Assistant.IntegrationTests.Infrastructure;
using Npgsql;

namespace Assistant.IntegrationTests.Schema;

/// <summary>
/// Test class for the <c>reminder_tasks</c> schema.
/// </summary>
/// <remarks>
/// Both tests here fabricate rows with raw SQL that no application code can currently
/// produce, because nothing writes to this table yet. They are the only proof that the two
/// check constraints in ReminderTaskConfiguration exist and carry the predicates we
/// intended: those predicates are hand-written SQL in C# string literals, so neither the
/// compiler nor EF validates them.
/// <para>
/// To decide whether one of them has become redundant, delete the matching
/// HasCheckConstraint call from ReminderTaskConfiguration, regenerate the migration, and run
/// the suite. If a test outside this class fails, the constraint is covered elsewhere and
/// the test below can go. If only the test below fails, it is still the sole guard, so keep
/// it.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// Retirement candidate at F2, which adds an application-level test for the same rule
    /// through AddAsync. That test only replaces this one if it reaches the database. If F2
    /// rejects Status.Unknown with a guard clause before the INSERT, this test stays the only
    /// thing that fails when the constraint is dropped. Apply the check in the class remarks.
    /// </remarks>
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
