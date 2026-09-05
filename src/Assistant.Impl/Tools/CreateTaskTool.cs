using System.Text.Json;
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Tools;

/// <summary>
/// Describes and executes the <c>create_task</c> tool offered to the model on every chat request.
/// </summary>
/// <param name="taskService">
/// Persists the task once its arguments are bound and any due time is resolved.
/// </param>
/// <param name="clock">Resolves the request's local-time text against the configured zone.</param>
/// <remarks>
/// The model is not bound by <see cref="ParametersJsonSchema"/>: a required field may be absent,
/// empty, or of the wrong shape, and the arguments text itself may not parse as JSON at all.
/// <see cref="ExecuteAsync"/> refuses all three before calling
/// <see cref="ITaskService.CreateAsync"/>, so nothing is persisted on any of those paths.
/// </remarks>
internal sealed class CreateTaskTool(ITaskService taskService, ILocalTimeResolver clock)
    : IAssistantTool
{
    /// <inheritdoc/>
    public string Name => "create_task";

    /// <inheritdoc/>
    public string Description =>
        "Create a task the user wants to be reminded about. Use this whenever the user mentions "
        + "something they need to do. Supply due_at_local whenever the user states or implies a "
        + "time.";

    /// <inheritdoc/>
    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "title": {
              "type": "string",
              "description": "Short description of what needs doing."
            },
            "due_at_local": {
              "type": "string",
              "description": "Absolute local datetime, ISO-8601 with no offset, e.g. 2026-08-31T10:00:00. Omit if the user gave no time."
            }
          },
          "required": ["title"]
        }
        """;

    /// <inheritdoc/>
    public async Task<Result<ReminderTask>> ExecuteAsync(string argumentsJson, CancellationToken ct)
    {
        CreateTaskRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreateTaskRequest>(argumentsJson);
        }
        catch (JsonException)
        {
            return Result<ReminderTask>.Failure(ErrorCode.ToolArgumentsMalformed);
        }

        if (request is null)
        {
            return Result<ReminderTask>.Failure(ErrorCode.ToolArgumentsMalformed);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result<ReminderTask>.Failure(ErrorCode.ToolArgumentMissing);
        }

        DateTimeOffset? dueAtUtc = null;

        if (request.DueAtLocal is not null)
        {
            var resolved = clock.Resolve(request.DueAtLocal);

            if (!resolved.IsSuccess)
            {
                return Result<ReminderTask>.Failure(resolved.Error!.Value);
            }

            dueAtUtc = resolved.Value;
        }

        return await taskService.CreateAsync(request, dueAtUtc, ct);
    }
}
