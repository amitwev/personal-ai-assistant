namespace Assistant.Interfaces;

/// <summary>
/// A unit of work the scheduler runs on a fixed interval.
/// </summary>
public interface IScheduledJob
{
    /// <summary>
    /// Runs the job once.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the run finishes, whether it did work or not.</returns>
    Task RunAsync(CancellationToken ct);
}
