using Assistant.Impl.Scheduling;

namespace Assistant.UnitTests.Scheduling;

/// <summary>
/// Test class for <see cref="ScheduledJobBase"/>.
/// </summary>
public sealed class ScheduledJobBaseTests
{
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When a job is already running
    /// And it is asked to run again
    /// Then the second request returns without starting a second run.
    /// </summary>
    [Fact]
    public async Task RunAsync_AlreadyRunning_SecondCallReturnsWithoutStartingASecondRun()
    {
        // Arrange
        var job = new BlockingJob();

        // Act
        var firstRun = job.RunAsync(CancellationToken.None);
        await job.Started.WaitAsync(SafetyNet);
        await job.RunAsync(CancellationToken.None).WaitAsync(SafetyNet);

        // Assert
        Assert.Equal(2, job.StartCount);

        job.Release();
        await firstRun;
    }

    /// <summary>
    /// A stub job whose execution signals that it started, then blocks until released, so a
    /// test can hold a run open long enough to attempt a second, overlapping call.
    /// </summary>
    private sealed class BlockingJob : ScheduledJobBase
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _startCount;

        public int StartCount => _startCount;

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _startCount);
            _started.TrySetResult();
            await _release.Task;
        }
    }
}
