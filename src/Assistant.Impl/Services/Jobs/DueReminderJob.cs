using Assistant.Impl.Scheduling;
using Assistant.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Impl.Services.Jobs;

/// <summary>
/// Delivers reminders whose due time has passed.
/// </summary>
/// <param name="scopeFactory">
/// Opens the scope <see cref="ITaskService"/> is resolved from, because this job is a singleton
/// and the service depends on the scoped database context.
/// </param>
/// <param name="notifier">Where a due reminder's message is delivered.</param>
/// <remarks>
/// Registered as a singleton so the re-entrancy guard on <see cref="ScheduledJobBase"/> refers to
/// a stable instance across ticks.
/// </remarks>
internal sealed class DueReminderJob(IServiceScopeFactory scopeFactory, INotifier notifier)
    : ScheduledJobBase
{
    private const int BatchSize = 50;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
        var tasks = await taskService.GetDueRemindersAsync(BatchSize, ct);

        foreach (var task in tasks)
        {
            await notifier.SendAsync(task.Title, ct);
            await taskService.MarkReminderSentAsync(task.Id, ct);
        }
    }
}
