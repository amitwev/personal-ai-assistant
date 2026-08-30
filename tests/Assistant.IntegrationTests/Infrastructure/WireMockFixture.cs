using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Assistant.IntegrationTests.Infrastructure;

/// <summary>
/// The stub API service defined in <c>compose.test.yaml</c>.
/// </summary>
/// <remarks>
/// The server runs in its own container, so nothing here can read its in-memory state. Requests
/// are read back over the admin API instead, and cleared between tests the way Respawn clears
/// tables — a shared stub accumulates requests exactly as a shared database accumulates rows.
/// </remarks>
public sealed class WireMockFixture : IAsyncLifetime
{
    private const string DefaultUrl = "http://localhost:58080";

    private static readonly Guid PendingUpdatesMapping =
        new("f7000000-0000-0000-0000-000000000001");

    private static readonly Guid DrainedUpdatesMapping =
        new("f7000000-0000-0000-0000-000000000002");

    private static readonly Guid AiMapping =
        new("f9a00000-0000-0000-0000-000000000001");

    private readonly HttpClient _http = new();

    /// <summary>
    /// The stub's base address.
    /// </summary>
    /// <value>
    /// The value of <c>ASSISTANT_TEST_STUB</c> when set, otherwise the fixed compose port.
    /// </value>
    public string Url { get; } =
        Environment.GetEnvironmentVariable("ASSISTANT_TEST_STUB") ?? DefaultUrl;

    /// <summary>
    /// Waits until the stub answers on its admin API.
    /// </summary>
    /// <returns>A task that completes once the stub is ready.</returns>
    /// <exception cref="InvalidOperationException">
    /// The stub did not answer within the 60 second deadline.
    /// </exception>
    public async Task InitializeAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await _http.GetAsync($"{Url}/__admin/mappings");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException(
            "The stub API did not become available within 60s. Run: docker compose -f compose.test.yaml up -d",
            last);
    }

    /// <summary>
    /// Forgets every request the stub has received.
    /// </summary>
    /// <returns>A task that completes once the request log is empty and any seeded mapping is gone.</returns>
    public async Task ResetAsync()
    {
        foreach (var id in new[] { PendingUpdatesMapping, DrainedUpdatesMapping, AiMapping })
        {
            (await _http.DeleteAsync($"{Url}/__admin/mappings/{id}")).Dispose();
        }

        (await _http.DeleteAsync($"{Url}/__admin/requests")).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Returns the send-message requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<SendMessagePayload>> SentMessagesAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/sendMessage", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<SendMessagePayload>(entry.Request.Body)!)
            .ToList();
    }

    /// <summary>
    /// Returns the chat-endpoint requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<AiRequestPayload>> AiRequestsAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/chat/completions", StringComparison.Ordinal)
                            && entry.Request.Method == "POST")
            .Select(entry => JsonSerializer.Deserialize<AiRequestPayload>(entry.Request.Body)!)
            .ToList();
    }

    /// <summary>
    /// Makes the stub serve the given updates to the next getUpdates poll.
    /// </summary>
    /// <param name="updates">The updates to serve, in the order Telegram would.</param>
    /// <returns>A task that completes once both mappings are installed.</returns>
    /// <remarks>
    /// Two mappings, drained by the offset in the request body rather than by a call
    /// count: once the caller polls with an offset past the last update it gets an
    /// empty result and keeps getting one, which is what real Telegram does. A
    /// listener that never advances its offset therefore keeps being served the same
    /// updates, which is the defect this shape exists to expose.
    /// </remarks>
    public async Task SeedUpdatesAsync(params InboundUpdate[] updates)
    {
        var pending = new JsonArray(updates.Select(u => (JsonNode)new JsonObject
        {
            ["update_id"] = u.UpdateId,
            ["message"] = new JsonObject
            {
                ["message_id"] = u.UpdateId,
                ["date"] = 1756000000L,
                ["chat"] = new JsonObject { ["id"] = u.ChatId, ["type"] = "private" },
                ["text"] = u.Text,
            },
        }).ToArray());

        var nextOffset = updates.Max(u => u.UpdateId) + 1;

        await PutMappingAsync(PendingUpdatesMapping, "/bot*/getUpdates", priority: 10,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject { ["ok"] = true, ["result"] = pending }, delayMs: null);

        await PutMappingAsync(DrainedUpdatesMapping, "/bot*/getUpdates", priority: 1,
            bodyPattern: $"*\"offset\":{nextOffset}*", statusCode: 200,
            responseBody: new JsonObject { ["ok"] = true, ["result"] = new JsonArray() },
            delayMs: 1000);
    }

    /// <summary>
    /// Makes the stub answer the next chat request with the given answer text.
    /// </summary>
    /// <param name="answer">The model's answer.</param>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedAiAnswerAsync(string answer) =>
        PutMappingAsync(AiMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = answer },
                }),
            },
            delayMs: null);

    /// <summary>
    /// Makes the stub answer the next chat request with a server error.
    /// </summary>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedAiFailureAsync() =>
        PutMappingAsync(AiMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 500,
            responseBody: new JsonObject { ["error"] = "stubbed provider failure" },
            delayMs: null);

    /// <summary>
    /// Makes the stub answer the next chat request with no candidate answers.
    /// </summary>
    /// <returns>A task that completes once the mapping is installed.</returns>
    public Task SeedAiNoAnswerAsync() =>
        PutMappingAsync(AiMapping, "/chat/completions", priority: 1,
            bodyPattern: null, statusCode: 200,
            responseBody: new JsonObject { ["choices"] = new JsonArray() },
            delayMs: null);

    /// <summary>
    /// Waits until the stub has received at least the given number of messages.
    /// </summary>
    /// <param name="count">How many messages to wait for.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns>Every message received, which may be more than requested.</returns>
    /// <exception cref="TimeoutException">Too few messages arrived in time.</exception>
    public async Task<IReadOnlyList<SendMessagePayload>> WaitForSentMessagesAsync(
        int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var sent = await SentMessagesAsync();

            if (sent.Count >= count)
            {
                return sent;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Expected at least {count} message(s) within {timeout.TotalSeconds:0.#}s; "
            + $"got {(await SentMessagesAsync()).Count}.");
    }

    /// <summary>
    /// Releases the HTTP client.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    private async Task PutMappingAsync(
        Guid id, string path, int priority, string? bodyPattern, int statusCode,
        JsonObject responseBody, int? delayMs)
    {
        var request = new JsonObject
        {
            ["Path"] = new JsonObject
            {
                ["Matchers"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "WildcardMatcher",
                    ["Pattern"] = path,
                }),
            },
            ["Methods"] = new JsonArray("POST"),
        };

        if (bodyPattern is not null)
        {
            request["Body"] = new JsonObject
            {
                ["Matcher"] = new JsonObject
                {
                    ["Name"] = "WildcardMatcher",
                    ["Pattern"] = bodyPattern,
                },
            };
        }

        var response = new JsonObject
        {
            ["StatusCode"] = statusCode,
            ["Headers"] = new JsonObject { ["Content-Type"] = "application/json" },
            ["Body"] = responseBody.ToJsonString(),
        };

        if (delayMs is not null)
        {
            response["Delay"] = delayMs;
        }

        var mapping = new JsonObject
        {
            ["Guid"] = id.ToString(),
            ["Priority"] = priority,
            ["Request"] = request,
            ["Response"] = response,
        };

        using var content = new StringContent(
            mapping.ToJsonString(), Encoding.UTF8, "application/json");

        (await _http.PostAsync($"{Url}/__admin/mappings", content)).EnsureSuccessStatusCode();
    }

    private sealed record AdminLogEntry(
        [property: JsonPropertyName("Request")] AdminRequest Request);

    private sealed record AdminRequest(
        [property: JsonPropertyName("Path")] string Path,
        [property: JsonPropertyName("Method")] string Method,
        [property: JsonPropertyName("Body")] string Body);
}

/// <summary>
/// The body of a Telegram <c>sendMessage</c> request.
/// </summary>
/// <param name="ChatId">The recipient.</param>
/// <param name="Text">The message body as it went over the wire.</param>
/// <param name="ParseMode">How Telegram is told to interpret the text.</param>
public sealed record SendMessagePayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>
    /// Null when the request carried exactly the three expected fields. Populated otherwise, which
    /// makes <c>Assert.Equivalent(strict: true)</c> fail — without this, extra fields are silently
    /// discarded during deserialisation and the assertion cannot see them.
    /// </value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// An inbound Telegram update, as <see cref="WireMockFixture.SeedUpdatesAsync"/> serves it.
/// </summary>
/// <param name="UpdateId">Telegram's identifier for the update.</param>
/// <param name="ChatId">The chat the message appears to come from.</param>
/// <param name="Text">The message body.</param>
public sealed record InboundUpdate(int UpdateId, long ChatId, string Text);

/// <summary>
/// The body of a chat request, as the assistant sends it.
/// </summary>
/// <param name="Model">The requested model slug.</param>
/// <param name="Messages">The conversation sent, system prompt first.</param>
/// <param name="MaxTokens">The token limit sent with the request.</param>
public sealed record AiRequestPayload(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<AiMessagePayload> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens)
{
    /// <summary>
    /// Any field on the wire that this record does not name.
    /// </summary>
    /// <value>Null when the request carried exactly the three expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// One message within a captured chat request.
/// </summary>
/// <param name="Role">Who is speaking: <c>system</c> or <c>user</c>.</param>
/// <param name="Content">What was said.</param>
public sealed record AiMessagePayload(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content)
{
    /// <summary>
    /// Any field on the wire that this message does not name.
    /// </summary>
    /// <value>Null when the message carried exactly the two expected fields.</value>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
