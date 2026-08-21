namespace Assistant.Interfaces;

/// <summary>
/// One unit of recurring work run by the scheduler.
/// </summary>
/// <remarks>
/// The scheduler resolves every implementation and runs each on every tick; it knows nothing
/// about what any of them do. Adding a job changes no existing type.
/// </remarks>
public interface IScheduledJob
{
    /// <summary>
    /// Name used in logs.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Performs one pass of the job's work.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task that completes when the pass is finished. Implementations decide for themselves
    /// whether a given tick is one on which they should act.
    /// </returns>
    /// <remarks>
    /// A pass that throws is logged and swallowed by the scheduler: one failing job must never
    /// stop the others or terminate the host.
    /// </remarks>
    Task RunAsync(CancellationToken ct);
}
