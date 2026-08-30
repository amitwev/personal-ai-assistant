using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Assistant.WireMock;

/// <summary>
/// The OpenAI-compatible chat endpoint this stub answers.
/// </summary>
/// <remarks>
/// The path is <c>/chat/completions</c> with no prefix: tests point <c>AiSettings.BaseUrl</c> at
/// this fixture's own address directly, while production points it at
/// <c>https://openrouter.ai/api/v1</c>, which already carries the version segment. The default
/// mapping answers at weak priority (100) so a locally-run worker never logs "No matching mapping
/// found"; tests install a stronger-priority mapping of their own.
/// </remarks>
internal static class AiStubs
{
    private const string DefaultAnswerResponse = """
        {"choices":[{"message":{"role":"assistant","content":"Stubbed answer."}}]}
        """;

    /// <summary>
    /// Installs the chat-endpoint mapping on the given server.
    /// </summary>
    /// <param name="server">The running stub server.</param>
    public static void Install(WireMockServer server)
    {
        server
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .AtPriority(100)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(DefaultAnswerResponse));
    }
}
