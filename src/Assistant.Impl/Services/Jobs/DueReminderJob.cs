using Assistant.Impl.Mapping;
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
/// <param name="clock">Renders a stored due instant back in the configured local zone.</param>
/// <remarks>
/// Registered as a singleton so the re-entrancy guard on <see cref="ScheduledJobBase"/> refers to
/// a stable instance across ticks.
/// </remarks>
internal sealed class DueReminderJob(
    IServiceScopeFactory scopeFactory, INotifier notifier, ILocalTimeResolver clock)
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
            var messageId = await notifier.AnnounceTaskAsync(
                task.MessageId, task.Id, task.ToMessageText(clock), ct);

            await taskService.RecordMessageAsync(task.Id, messageId, ct);
            await taskService.MarkReminderSentAsync(task.Id, ct);
        }
    }
}
