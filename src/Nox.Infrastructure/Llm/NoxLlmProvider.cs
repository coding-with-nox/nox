using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Llm;
using System.Runtime.CompilerServices;

namespace Nox.Infrastructure.Llm;

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

        // Initialize Claude client
        var anthropicKey = config["Nox:Llm:Providers:Anthropic:ApiKey"] ?? string.Empty;
        if (!string.IsNullOrEmpty(anthropicKey))
        {
            var anthropic = new AnthropicClient(anthropicKey);
            _chatClients[LlmModel.Claude4] = new AnthropicChatClientAdapter(anthropic.Messages, "claude-opus-4-5-20251101");
            _chatClients[LlmModel.Claude3Sonnet] = new AnthropicChatClientAdapter(anthropic.Messages, "claude-sonnet-4-5");
        }

        // Initialize OpenAI client if configured (placeholder)
        // var openAiKey = config["Nox:Llm:Providers:OpenAI:ApiKey"] ?? string.Empty;

        // Default embedding generator (placeholder until a real one is configured)
        _embeddingGenerator = new NoOpEmbeddingGenerator();
    }

    public IChatClient GetChatClient(LlmModel model)
    {
        if (_chatClients.TryGetValue(model, out var client))
            return client;

        // Fallback to Claude4
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
        // Rough approximation: 1 token ≈ 4 characters
        await Task.CompletedTask;
        return text.Length / 4;
    }

    public async Task<bool> IsWithinBudgetAsync(Guid agentId, int estimatedTokens, CancellationToken ct = default)
    {
        // TODO: integrate with agent token tracking
        await Task.CompletedTask;
        return estimatedTokens < 120_000;
    }
}

/// <summary>Bridges Anthropic.SDK 4.x MessagesEndpoint to IChatClient (M.Extensions.AI 9.x).</summary>
internal sealed class AnthropicChatClientAdapter(MessagesEndpoint endpoint, string modelId) : IChatClient
{
    public ChatClientMetadata Metadata => new("anthropic", null, modelId);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var anthropicMessages = messages
            .Select(m => new Anthropic.SDK.Messaging.Message(
                m.Role == ChatRole.User ? RoleType.User : RoleType.Assistant,
                m.Text ?? string.Empty))
            .ToList();

        var parameters = new MessageParameters
        {
            Messages = anthropicMessages,
            MaxTokens = options?.MaxOutputTokens ?? 8192,
            Model = modelId,
            Stream = false,
            Temperature = (decimal)(options?.Temperature ?? 1.0)
        };

        var result = await endpoint.GetClaudeMessageAsync(parameters, cancellationToken);
        var text = result.Message?.ToString() ?? string.Empty;
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
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
}

/// <summary>Placeholder embedding generator until a real one is configured.</summary>
internal class NoOpEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata => new("noop", null, "noop");

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values
            .Select(_ => new Embedding<float>(new float[1536]))
            .ToList();
        await Task.CompletedTask;
        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
