using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Services;

/// <summary>
/// The single writer for tasks.
/// </summary>
/// <param name="repository">Persistence for tasks.</param>
/// <param name="timeProvider">The current instant.</param>
/// <remarks>
/// Every rule that governs a task's lifecycle lives here, because the models are anemic by design
/// and have no other enforcement point. A caller that mutated a task itself could set one field
/// without its pair — which is exactly how a task stops reminding forever.
/// </remarks>
internal sealed class TaskService(ITaskRepository repository, TimeProvider timeProvider) : ITaskService
{
    /// <inheritdoc/>
    public async Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct)
    {
        var task = await repository.FindAsync(id, ct);

        if (task is null)
        {
            return Result.Failure(ErrorCode.TaskNotFound);
        }

        if (task.DueAt is null)
        {
            return Result.Failure(ErrorCode.DueTimeMissing);
        }

        var now = timeProvider.GetUtcNow();

        task.ReminderSentAt = now;
        task.UpdatedAt = now;
        await repository.UpdateAsync(task, ct);

        return Result.Success();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(int limit, CancellationToken ct) =>
        repository.GetDueRemindersAsync(timeProvider.GetUtcNow(), limit, ct);
}
