using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.WireMock;

/// <summary>
/// The Telegram Bot API endpoints this stub answers.
/// </summary>
internal static class TelegramStubs
{
    private const string SendMessageResponse = """
        {"ok":true,"result":{"message_id":1,"date":1756000000,
         "chat":{"id":1,"type":"private"},"text":"stubbed"}}
        """;

    /// <summary>
    /// Installs the Telegram mappings on the given server.
    /// </summary>
    /// <param name="server">The running stub server.</param>
    /// <remarks>
    /// The path is <c>/bot*/sendMessage</c> because the SDK puts the bot token in the path, so
    /// whichever token a test uses must not affect matching. The response is the real envelope
    /// shape: the client deserialises it, and a bare object makes it throw.
    /// </remarks>
    public static void Install(WireMockServer server) =>
        server
            .Given(Request.Create().WithPath("/bot*/sendMessage").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(SendMessageResponse));
}
