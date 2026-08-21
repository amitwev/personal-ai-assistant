namespace Assistant.Worker;

/// <summary>
/// Placeholder background worker scaffolded by the project template. Slice 1 replaces
/// this with the composition root's hosted services.
/// </summary>
/// <param name="logger">Logger used to record worker activity.</param>
public class Worker(ILogger<Worker> logger) : BackgroundService
{
    /// <summary>
    /// Runs the worker's background loop until cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">Token observed to stop the loop.</param>
    /// <returns>A task that completes when the loop exits.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
