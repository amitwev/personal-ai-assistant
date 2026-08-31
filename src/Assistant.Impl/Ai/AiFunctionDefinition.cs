using System.Text.Json.Nodes;

namespace Assistant.Impl.Ai;

/// <summary>
/// The name, description and parameter schema of one tool definition sent on a request.
/// </summary>
/// <param name="Name">The tool's name, echoed back on any call the model makes to it.</param>
/// <param name="Description">What the tool does, written for the model rather than a developer.</param>
/// <param name="Parameters">The JSON Schema object describing the tool's arguments.</param>
/// <remarks>
/// <see cref="Parameters"/> is a <see cref="JsonNode"/>, not a <see cref="System.Text.Json.JsonElement"/>
/// parsed from a <see cref="System.Text.Json.JsonDocument"/>: a <see cref="JsonNode"/> is fully
/// garbage-collected and carries no disposal lifetime to trip over, where a <c>JsonElement</c>
/// stops being readable the moment its parent <c>JsonDocument</c> is disposed.
/// <c>WireMockFixture</c> already builds its own seeded payloads from the same
/// <see cref="System.Text.Json.Nodes"/> types, for the same reason.
/// </remarks>
internal sealed record AiFunctionDefinition(string Name, string Description, JsonNode Parameters);
