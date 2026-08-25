using Assistant.Impl.Scheduling;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Assistant.UnitTests.Scheduling;

/// <summary>
/// Test class for <see cref="ReminderScheduler"/>.
/// </summary>
public sealed class ReminderSchedulerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When a job throws on one tick
    /// And the next tick arrives
    /// Then the job runs again.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_JobThrowsOnOneTick_RunsAgainOnNextTick()
    {
        // Arrange
        var job = new ThrowsOnFirstRunJob();
        var fakeTime = new FakeTimeProvider();
        var timeProvider = new ArmSignallingTimeProvider(fakeTime);
        var scheduler = new ReminderScheduler([job], timeProvider, NullLogger<ReminderScheduler>.Instance);

        // Act
        await scheduler.StartAsync(CancellationToken.None);
        await timeProvider.Armed.WaitAsync(SafetyNet);
        fakeTime.Advance(Interval);
        await job.Ran(1).WaitAsync(SafetyNet);
        fakeTime.Advance(Interval);
        await job.Ran(2).WaitAsync(SafetyNet);
        await scheduler.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, job.RunCount);
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> that forwards to a <see cref="FakeTimeProvider"/> but also
    /// signals the moment something first calls <see cref="CreateTimer"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Microsoft.Extensions.Hosting.BackgroundService.StartAsync"/> dispatches
    /// <c>ExecuteAsync</c> via <c>Task.Run</c> rather than running it inline, so there is no
    /// guarantee the scheduler has even constructed its <see cref="PeriodicTimer"/> — which is
    /// what calls <see cref="CreateTimer"/> — by the time a test's own continuation resumes after
    /// <c>StartAsync</c> returns. Advancing the fake clock before that registration happens would
    /// be silently lost: nothing is listening for it yet. Waiting on <see cref="Armed"/> first
    /// removes that race without guessing at how long the dispatch takes.
    /// </remarks>
    private sealed class ArmSignallingTimeProvider(FakeTimeProvider inner) : TimeProvider
    {
        private readonly TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Armed => _armed.Task;

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        /// <inheritdoc/>
        public override long GetTimestamp() => inner.GetTimestamp();

        /// <inheritdoc/>
        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        /// <inheritdoc/>
        public override long TimestampFrequency => inner.TimestampFrequency;

        /// <inheritdoc/>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _armed.TrySetResult();
            return inner.CreateTimer(callback, state, dueTime, period);
        }
    }

    /// <summary>
    /// A stub job that throws the first time it runs and records each run so a test can await
    /// a specific run rather than racing the loop's thread-pool continuation.
    /// </summary>
    private sealed class ThrowsOnFirstRunJob : IScheduledJob
    {
        private readonly TaskCompletionSource _firstRun =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _secondRun =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _runCount;

        public int RunCount => _runCount;

        public Task Ran(int run) => run switch
        {
            1 => _firstRun.Task,
            2 => _secondRun.Task,
            _ => throw new ArgumentOutOfRangeException(nameof(run), run, "Only runs 1 and 2 are recorded."),
        };

        public Task RunAsync(CancellationToken ct)
        {
            var count = Interlocked.Increment(ref _runCount);

            if (count == 1)
            {
                _firstRun.TrySetResult();
                throw new InvalidOperationException("Boom.");
            }

            _secondRun.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
