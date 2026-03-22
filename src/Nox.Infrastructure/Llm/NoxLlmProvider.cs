using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Llm;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Llm;

// Disambiguate Anthropic types vs M.Extensions.AI types
using AnthropicTextContent = Anthropic.SDK.Messaging.TextContent;
using AiTextContent = Microsoft.Extensions.AI.TextContent;
using AnthropicToolResultContent = Anthropic.SDK.Messaging.ToolResultContent;

public class NoxLlmProvider : ILlmProvider
{
    private readonly IConfiguration _config;
    private readonly ILogger<NoxLlmProvider> _logger;
    private readonly Dictionary<LlmModel, IChatClient> _chatClients = new();
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public NoxLlmProvider(IConfiguration config, ILogger<NoxLlmProvider> logger)
    {
        _config = config;
        _logger = logger;

        var anthropicKey = config["Nox:Llm:Providers:Anthropic:ApiKey"] ?? string.Empty;
        if (!string.IsNullOrEmpty(anthropicKey))
        {
            var anthropic = new AnthropicClient(anthropicKey);
            _chatClients[LlmModel.Claude4] = new AnthropicChatClientAdapter(anthropic.Messages, "claude-opus-4-6");
            _chatClients[LlmModel.Claude3Sonnet] = new AnthropicChatClientAdapter(anthropic.Messages, "claude-sonnet-4-6");
        }

        _embeddingGenerator = new NoOpEmbeddingGenerator();
    }

    public IChatClient GetChatClient(LlmModel model)
    {
        if (_chatClients.TryGetValue(model, out var client))
            return client;

        if (_chatClients.TryGetValue(LlmModel.Claude4, out var fallback))
        {
            _logger.LogWarning("Model {Model} not configured, falling back to Claude4", model);
            return fallback;
        }

        throw new InvalidOperationException($"No LLM client configured for {model} or fallback");
    }

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(LlmModel? model = null)
        => _embeddingGenerator;

    public async Task<int> CountTokensAsync(string text, LlmModel model, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return text.Length / 4; // ~4 chars per token approximation
    }

    public async Task<bool> IsWithinBudgetAsync(Guid agentId, int estimatedTokens, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return estimatedTokens < 120_000;
    }
}

/// <summary>
/// Bridges Anthropic.SDK 4.x to IChatClient (M.Extensions.AI 9.x).
/// Handles: system messages, tool definitions (via AIFunction → Common.Function),
/// tool use blocks, tool results, usage metrics.
/// </summary>
internal sealed class AnthropicChatClientAdapter(MessagesEndpoint endpoint, string modelId) : IChatClient
{
    public ChatClientMetadata Metadata => new("anthropic", null, modelId);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var msgList = messages.ToList();

        // 1. Extract system messages
        var systemText = string.Join("\n\n", msgList
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? string.Empty)
            .Where(s => s.Length > 0));

        // 2. Convert non-system messages to Anthropic format
        var anthropicMessages = new List<Message>();
        foreach (var m in msgList.Where(m => m.Role != ChatRole.System))
        {
            if (m.Role == ChatRole.Tool)
            {
                // Tool results → User message with ToolResultContent blocks
                var blocks = m.Contents.OfType<FunctionResultContent>()
                    .Select(r => (ContentBase)new AnthropicToolResultContent
                    {
                        ToolUseId = r.CallId ?? string.Empty,
                        Content = r.Result?.ToString() ?? string.Empty
                    }).ToList();

                if (blocks.Count > 0)
                    anthropicMessages.Add(new Message { Role = RoleType.User, Content = blocks });
            }
            else if (m.Role == ChatRole.Assistant)
            {
                var blocks = new List<ContentBase>();
                if (!string.IsNullOrWhiteSpace(m.Text))
                    blocks.Add(new AnthropicTextContent { Text = m.Text });

                foreach (var fc in m.Contents.OfType<FunctionCallContent>())
                {
                    blocks.Add(new ToolUseContent
                    {
                        Id = fc.CallId ?? Guid.NewGuid().ToString("N"),
                        Name = fc.Name,
                        Input = fc.Arguments is not null
                            ? JsonNode.Parse(JsonSerializer.Serialize(fc.Arguments))
                            : new JsonObject()
                    });
                }

                if (blocks.Count > 0)
                    anthropicMessages.Add(new Message { Role = RoleType.Assistant, Content = blocks });
            }
            else
            {
                anthropicMessages.Add(new Message(RoleType.User, m.Text ?? string.Empty));
            }
        }

        // 3. Build Anthropic tools from ChatOptions.Tools (AIFunction → Common.Tool via Function)
        var anthropicTools = new List<Anthropic.SDK.Common.Tool>();
        if (options?.Tools is not null)
        {
            foreach (var tool in options.Tools.OfType<AIFunction>())
                anthropicTools.Add(ConvertToAnthropicTool(tool));
        }

        // 4. Call the Anthropic API
        var parameters = new MessageParameters
        {
            Messages = anthropicMessages,
            SystemMessage = systemText.Length > 0 ? systemText : null,
            MaxTokens = options?.MaxOutputTokens ?? 8192,
            Model = modelId,
            Stream = false,
            Temperature = (decimal)(options?.Temperature ?? 1.0),
            Tools = anthropicTools.Count > 0 ? anthropicTools : null
        };

        var result = await endpoint.GetClaudeMessageAsync(parameters, cancellationToken);

        // 5. Parse response content blocks
        var responseContents = new List<AIContent>();
        foreach (var block in result.Content ?? [])
        {
            switch (block)
            {
                case AnthropicTextContent tc:
                    responseContents.Add(new AiTextContent(tc.Text));
                    break;
                case ToolUseContent tuc:
                    var args = tuc.Input is not null
                        ? JsonSerializer.Deserialize<Dictionary<string, object?>>(tuc.Input.ToJsonString())
                        : null;
                    responseContents.Add(new FunctionCallContent(tuc.Id, tuc.Name, args));
                    break;
            }
        }

        var responseMessage = new ChatMessage(ChatRole.Assistant, responseContents);
        var response = new ChatResponse(responseMessage)
        {
            FinishReason = result.StopReason switch
            {
                "tool_use" => ChatFinishReason.ToolCalls,
                "max_tokens" => ChatFinishReason.Length,
                _ => ChatFinishReason.Stop
            }
        };

        if (result.Usage is not null)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = result.Usage.InputTokens,
                OutputTokenCount = result.Usage.OutputTokens,
                TotalTokenCount = result.Usage.InputTokens + result.Usage.OutputTokens
            };
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text ?? string.Empty);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

    /// <summary>
    /// Converts an AIFunction (M.Extensions.AI) to Anthropic Common.Tool via Function + implicit cast.
    /// Uses the AIFunction.JsonSchema to build the parameters JsonNode.
    /// </summary>
    private static Anthropic.SDK.Common.Tool ConvertToAnthropicTool(AIFunction func)
    {
        // Build Anthropic-compatible JSON schema for the function parameters
        var schemaJson = func.JsonSchema.GetRawText();
        var parametersNode = JsonNode.Parse(schemaJson) ?? new JsonObject();

        // Function(name, description, JsonNode parameters) → implicit Tool cast
        Anthropic.SDK.Common.Function function = new(func.Name, func.Description, parametersNode);
        return function; // implicit operator Tool.op_Implicit(Function)
    }
}

/// <summary>Placeholder embedding generator — replace with Voyage/OpenAI for production.</summary>
internal class NoOpEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata => new("noop", null, "noop");

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(_ => new Embedding<float>(new float[1536])).ToList();
        await Task.CompletedTask;
        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
