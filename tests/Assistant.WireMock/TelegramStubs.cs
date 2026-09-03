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

    private const string NoUpdatesResponse = """{"ok":true,"result":[]}""";

    private const string AnswerCallbackQueryResponse = """{"ok":true,"result":true}""";

    private const string EditMessageTextResponse = """
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
    ///
    /// The <c>getUpdates</c> mapping exists so a listener with nothing seeded for it does not
    /// see "No matching mapping found" — without it, a locally-run worker would fail to
    /// deserialise the stub's fallback response and log an error every poll, forever. It answers
    /// with an empty result at the weakest priority, so any test-seeded mapping wins over it, and
    /// with a one-second delay so an idle listener polls at roughly once per second instead of
    /// spinning at full speed against an instantly-answering stub.
    /// </remarks>
    public static void Install(WireMockServer server)
    {
        server
            .Given(Request.Create().WithPath("/bot*/sendMessage").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(SendMessageResponse));

        server
            .Given(Request.Create().WithPath("/bot*/answerCallbackQuery").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(AnswerCallbackQueryResponse));

        server
            .Given(Request.Create().WithPath("/bot*/editMessageText").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(EditMessageTextResponse));

        server
            .Given(Request.Create().WithPath("/bot*/getUpdates").UsingPost())
            .AtPriority(100)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(NoUpdatesResponse)
                .WithDelay(TimeSpan.FromSeconds(1)));
    }
}
