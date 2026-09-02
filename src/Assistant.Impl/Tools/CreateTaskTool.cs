using Assistant.Interfaces;

namespace Assistant.Impl.Tools;

/// <summary>
/// Describes the <c>create_task</c> tool offered to the model on every chat request.
/// </summary>
/// <remarks>
/// Carries no behaviour: the model can call this tool and <c>AiClient</c> parses the call out of
/// the answer, but nothing dispatches to an implementation or writes a row until
/// <see cref="ITaskService"/> grows a create method at F10. <c>due_at_local</c> is the only
/// optional field the schema advertises today; <c>notes</c> and <c>priority</c> wait for the
/// features that give <c>ReminderTask</c> somewhere to put them.
/// </remarks>
internal sealed class CreateTaskTool : IAssistantTool
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
}
