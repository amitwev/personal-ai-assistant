using Assistant.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Scheduling;

/// <summary>
/// Runs every registered job on a fixed interval for as long as the host is up.
/// </summary>
/// <param name="jobs">The jobs to run each tick.</param>
/// <param name="timeProvider">Supplies the timer's notion of time.</param>
/// <param name="logger">Where a job's exception is recorded.</param>
/// <remarks>
/// A throwing job must never terminate the loop or the host, so every job runs inside its own
/// try/catch here rather than in <see cref="ScheduledJobBase"/> — a job that implements
/// <see cref="IScheduledJob"/> directly, which the interface permits, would otherwise be able to
/// take the whole host down. The re-entrancy guard is a different promise and lives on the base
/// class instead.
/// </remarks>
internal sealed class ReminderScheduler(
    IEnumerable<IScheduledJob> jobs, TimeProvider timeProvider, ILogger<ReminderScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var job in jobs)
                {
                    try
                    {
                        await job.RunAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Scheduled job {Job} threw; the loop continues.", job.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure.
        }
    }
}
