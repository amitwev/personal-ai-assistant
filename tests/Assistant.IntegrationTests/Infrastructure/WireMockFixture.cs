using System.Net.Http.Json;
using System.Text.Json;
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
    /// <returns>A task that completes once the request log is empty.</returns>
    public async Task ResetAsync() =>
        (await _http.DeleteAsync($"{Url}/__admin/requests")).EnsureSuccessStatusCode();

    /// <summary>
    /// Returns the send-message requests the stub received, in order.
    /// </summary>
    /// <returns>One payload per captured request.</returns>
    public async Task<IReadOnlyList<SendMessagePayload>> SentMessagesAsync()
    {
        var entries = await _http.GetFromJsonAsync<List<AdminLogEntry>>($"{Url}/__admin/requests")
                      ?? [];

        return entries
            .Where(entry => entry.Request.Path.EndsWith("/sendMessage", StringComparison.Ordinal))
            .Select(entry => JsonSerializer.Deserialize<SendMessagePayload>(entry.Request.Body)!)
            .ToList();
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

    private sealed record AdminLogEntry(
        [property: JsonPropertyName("Request")] AdminRequest Request);

    private sealed record AdminRequest(
        [property: JsonPropertyName("Path")] string Path,
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
    [property: JsonPropertyName("parse_mode")] string ParseMode);
