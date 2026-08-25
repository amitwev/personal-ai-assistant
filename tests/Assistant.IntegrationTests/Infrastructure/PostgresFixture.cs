using Assistant.Impl;
using Assistant.Interfaces;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// Shared connection to the Postgres instance defined in <c>compose.test.yaml</c>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=55432;Database=assistant_test;Username=assistant;Password=assistant;Include Error Detail=true";

    private Respawner? _respawner;

    /// <summary>
    /// The connection string tests should use to reach the compose Postgres instance.
    /// </summary>
    /// <value>
    /// The value of the <c>ASSISTANT_TEST_DB</c> environment variable when set, otherwise a
    /// default pointing at the fixed compose port.
    /// </value>
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ASSISTANT_TEST_DB") ?? DefaultConnectionString;

    /// <summary>
    /// Waits for the server, migrates the schema, and prepares the table-reset mechanism.
    /// </summary>
    /// <returns>A task that completes once the fixture is ready for tests to use.</returns>
    public async Task InitializeAsync()
    {
        await WaitForServerAsync();

        var services = new ServiceCollection();
        services.AddAssistantRepository(ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await provider.MigrateAssistantDatabaseAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("public", "__EFMigrationsHistory")],
        });
    }

    /// <summary>
    /// Truncates every table, leaving the schema in place.
    /// </summary>
    /// <returns>A task that completes once every table has been reset.</returns>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    /// <summary>
    /// Builds a service provider wired to the test database.
    /// </summary>
    /// <returns>
    /// A provider the caller owns and must dispose. Each call produces an independent
    /// <c>DbContext</c>, which is what lets a test read a row back without the change tracker
    /// answering from memory.
    /// </returns>
    public ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddAssistantRepository(ConnectionString);
        services.AddAssistantServices();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Saves a task through a provider of its own, then disposes it.
    /// </summary>
    /// <param name="reminderTask">The task to save.</param>
    /// <returns>A task that completes once the row is written and the context is gone.</returns>
    /// <remarks>
    /// Arrangement, not the act. Writing through a separate context is what stops the change
    /// tracker answering a later read from memory, which would turn an assertion about the
    /// database into a comparison of an object with itself.
    /// </remarks>
    public async Task SaveAsync(ReminderTask reminderTask)
    {
        await using var writer = CreateProvider();
        await writer.GetRequiredService<ITaskRepository>()
            .AddAsync(reminderTask, CancellationToken.None);
    }

    /// <summary>
    /// No resources to release; the shared connection is opened per operation.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Polls until the server accepts a connection. Compose returning does not mean Postgres is
    /// listening, and this is the single most common cause of a flaky first test in CI.
    /// </summary>
    /// <returns>A task that completes once a connection succeeds.</returns>
    /// <exception cref="InvalidOperationException">
    /// The server did not accept a connection within the 60 second deadline.
    /// </exception>
    private async Task WaitForServerAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500);
            }
        }

        throw new InvalidOperationException(
            "Postgres did not become available within 60s. Run: docker compose -f compose.test.yaml up -d",
            last);
    }
}
