using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services.Actions;

/// <summary>
/// Completes a task in response to its Done button being tapped.
/// </summary>
/// <param name="taskService">The single writer for tasks.</param>
internal sealed class DoneAction(ITaskService taskService) : ITaskAction
{
    /// <inheritdoc/>
    public TaskActionDefinition Definition => TaskActions.Done;

    /// <inheritdoc/>
    public Task<Result> ExecuteAsync(Guid taskId, CancellationToken ct) =>
        taskService.CompleteAsync(taskId, ct);
}
