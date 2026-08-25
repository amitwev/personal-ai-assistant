using Assistant.Interfaces;

namespace Assistant.Impl.Scheduling;

/// <summary>
/// A scheduled job that refuses to overlap itself.
/// </summary>
/// <remarks>
/// The guard is per instance: a re-entrant call is detected only against the same object, so a
/// job must be registered as a singleton for the guard to mean anything. A fresh instance per
/// call — for example one resolved from a new DI scope on every tick — starts with a fresh flag
/// and guards nothing.
/// </remarks>
internal abstract class ScheduledJobBase : IScheduledJob
{
    private const int Idle = 0;
    private const int Running = 1;

    private int _state = Idle;

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _state, Running, Idle) != Idle)
        {
            return;
        }

        try
        {
            await ExecuteAsync(ct);
        }
        finally
        {
            Interlocked.Exchange(ref _state, Idle);
        }
    }

    /// <summary>
    /// Does the job's actual work.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the work finishes.</returns>
    protected abstract Task ExecuteAsync(CancellationToken ct);
}
