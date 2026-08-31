using System.Text.Json.Nodes;
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Impl.Ai;

/// <summary>
/// Reaches the configured chat endpoint with the owner's text, the system prompt and every
/// registered tool definition, and returns the tool call the model chose.
/// </summary>
/// <param name="api">The Refit client for the chat endpoint.</param>
/// <param name="prompt">Builds the system prompt sent as the first message.</param>
/// <param name="settings">The configured model and token limit.</param>
/// <param name="tools">Every tool definition offered to the model on the request.</param>
/// <param name="logger">Where a provider failure or an empty answer is recorded.</param>
internal sealed class AiClient(
    IAiApi api, SystemPrompt prompt, AiSettings settings, IEnumerable<IAssistantTool> tools,
    ILogger<AiClient> logger) : IAiClient
{
    /// <inheritdoc/>
    public async Task<Result<ToolCall>> AskAsync(string userText, CancellationToken ct)
    {
        AiResponse response;
        try
        {
            response = await api.AskAsync(
                new AiRequest(
                    settings.Model,
                    [new AiMessage("system", prompt.Build()),
                     new AiMessage("user", userText)],
                    settings.MaxTokens,
                    tools.Select(ToWireTool).ToList()),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reaching the chat model failed.");
            return Result<ToolCall>.Failure(ErrorCode.ModelUnavailable);
        }

        var choice = response.Choices.FirstOrDefault();

        if (choice is null)
        {
            logger.LogError("The chat model returned no answer.");
            return Result<ToolCall>.Failure(ErrorCode.ModelReturnedNoAnswer);
        }

        var call = choice.Message.ToolCalls?.FirstOrDefault();

        if (call is null)
        {
            logger.LogError("The chat model replied without calling a tool.");
            return Result<ToolCall>.Failure(ErrorCode.ModelReturnedNoToolCall);
        }

        logger.LogInformation(
            "The chat model called {Tool} with {Arguments}.",
            call.Function.Name,
            call.Function.Arguments);

        return Result<ToolCall>.Success(new ToolCall(call.Function.Name, call.Function.Arguments));
    }

    private static AiTool ToWireTool(IAssistantTool tool) =>
        new("function", new AiFunctionDefinition(
            tool.Name, tool.Description, JsonNode.Parse(tool.ParametersJsonSchema)!));
}
