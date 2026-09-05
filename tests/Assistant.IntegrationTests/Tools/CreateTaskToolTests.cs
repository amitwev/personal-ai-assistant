using Assistant.Contracts;
using Assistant.Impl;
using Assistant.Impl.Settings;
using Assistant.Impl.Tools;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Assistant.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.IntegrationTests.Tools;

/// <summary>
/// Test class for <see cref="CreateTaskTool"/>, resolved as the registered
/// <see cref="IAssistantTool"/> it is.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
[Collection(IntegrationCollection.Name)]
public sealed class CreateTaskToolTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int NoLimit = 100;

    private static readonly DateTimeOffset AsOf = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider _provider = null!;

    private IAssistantTool _sut = null!;

    private ITaskRepository _repository = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddAssistantRepository(postgres.ConnectionString);
        services.AddAssistantServices();
        services.AddAssistantTime(new TimeSettings { IanaTimeZone = "Asia/Jerusalem" });
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));
        services.AddScoped<IAssistantTool, CreateTaskTool>();
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IEnumerable<IAssistantTool>>()
            .Single(tool => tool.Name == "create_task");
        _repository = _provider.GetRequiredService<ITaskRepository>();

        await postgres.ResetAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When the model calls create_task with a title and a due time that resolves
    /// And the call is executed
    /// Then a pending task is stored with that title and the resolved UTC instant
    /// And the same task is handed back to the caller.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TitleAndAResolvableDueTime_StoresAPendingTaskAndReturnsIt()
    {
        // Arrange
        const string argumentsJson =
            """{"title":"Call the bank","due_at_local":"2026-08-26T10:00:00"}""";

        // Act
        var result = await _sut.ExecuteAsync(argumentsJson, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Call the bank", result.Value!.Title);
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 7, 0, 0, TimeSpan.Zero), result.Value.DueAt);
        Assert.Equal(ReminderStatus.Pending, result.Value.Status);

        var stored = await _repository.FindAsync(result.Value.Id, CancellationToken.None);
        Assert.Equal(result.Value.DueAt, stored!.DueAt);
    }

    /// <summary>
    /// When the model calls create_task with a title and no due time
    /// And the call is executed
    /// Then a task is stored with no due instant
    /// And it never appears among tasks that are due.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoDueTime_StoresATaskThatIsNeverDue()
    {
        // Arrange
        const string argumentsJson = """{"title":"Buy milk"}""";

        // Act
        var result = await _sut.ExecuteAsync(argumentsJson, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DueAt);

        var due = await _repository.GetDueRemindersAsync(AsOf.AddYears(10), NoLimit, CancellationToken.None);
        Assert.DoesNotContain(due, t => t.Id == result.Value.Id);
    }

    /// <summary>
    /// When the model calls create_task with a due time more than a minute in the past
    /// And the call is executed
    /// Then it is refused as a due time in the past
    /// And nothing is handed back to persist.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DueTimeInThePast_IsRejected()
    {
        // Arrange
        const string argumentsJson =
            """{"title":"Call the bank","due_at_local":"2026-08-25T10:00:00"}""";

        // Act
        var result = await _sut.ExecuteAsync(argumentsJson, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.DueTimeInPast, result.Error);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// When the model calls create_task with a due time more than two years ahead
    /// And the call is executed
    /// Then it is refused as too far ahead
    /// And nothing is handed back to persist.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DueTimeTooFarAhead_IsRejected()
    {
        // Arrange
        const string argumentsJson =
            """{"title":"Call the bank","due_at_local":"2029-06-01T00:00:00"}""";

        // Act
        var result = await _sut.ExecuteAsync(argumentsJson, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.DueTimeTooFarAhead, result.Error);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// When the model calls create_task with due time text the resolver cannot parse
    /// And the call is executed
    /// Then it is refused as unparseable
    /// And nothing is handed back to persist.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DueTimeCannotBeParsed_IsRejected()
    {
        // Arrange
        const string argumentsJson = """{"title":"Call the bank","due_at_local":"not a date"}""";

        // Act
        var result = await _sut.ExecuteAsync(argumentsJson, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.DueTimeUnparseable, result.Error);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// When the model calls create_task with no title, an empty title, or a whitespace-only title
    /// And the call is executed
    /// Then it is refused as missing a required argument
    /// And nothing is handed back to persist.
    /// </summary>
    /// <param name="argumentsJson">Arguments carrying no usable title.</param>
    [Theory]
    [InlineData("""{"due_at_local":"2026-08-26T10:00:00"}""")]
    [InlineData("""{"title":""}""")]
    [InlineData("""{"title":"   "}""")]
    public async Task ExecuteAsync_TitleMissingEmptyOrBlank_IsRejected(string argumentsJson)
    {
        // Act
        var result = await _sut.ExecuteAsync(argumentsJson, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ToolArgumentMissing, result.Error);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// When the model calls create_task with arguments that are not a usable JSON object
    /// And the call is executed
    /// Then it is refused as malformed
    /// And nothing is handed back to persist.
    /// </summary>
    /// <param name="argumentsJson">Text that cannot bind to a create_task request.</param>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("null")]
    public async Task ExecuteAsync_ArgumentsAreNotAUsableObject_IsRejected(string argumentsJson)
    {
        // Act
        var result = await _sut.ExecuteAsync(argumentsJson, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ToolArgumentsMalformed, result.Error);
        Assert.Null(result.Value);
    }
}
