using System.Text.Json.Serialization;

namespace Assistant.Contracts;

/// <summary>
/// The arguments the model supplies on a <c>create_task</c> tool call.
/// </summary>
/// <param name="Title">Short description of what needs doing.</param>
/// <param name="DueAtLocal">
/// An absolute local datetime as the model returns it: ISO-8601 with no offset, for example
/// <c>2026-09-01T10:00:00</c>. <see langword="null"/> when the user gave no time. Stays a
/// string rather than a parsed <see cref="DateTime"/>: resolving it against the configured zone
/// is <c>ILocalTimeResolver.Resolve</c>'s job, arriving at F10.
/// </param>
/// <remarks>
/// Property names carry explicit <see cref="JsonPropertyNameAttribute"/> values because nothing
/// deserialising a <see cref="ToolCall.ArgumentsJson"/> string applies a naming policy — unlike
/// <c>IAiApi</c>'s own traffic, which goes through Refit's configured snake-case serializer.
/// <c>WireMockFixture</c>'s own payload records use the identical pattern for the identical
/// reason.
/// </remarks>
public sealed record CreateTaskRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("due_at_local")] string? DueAtLocal);
