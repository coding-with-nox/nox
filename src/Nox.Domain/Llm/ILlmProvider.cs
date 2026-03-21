using Microsoft.Extensions.AI;

namespace Nox.Domain.Llm;

public interface ILlmProvider
{
    IChatClient GetChatClient(LlmModel model);
    IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(LlmModel? model = null);
    Task<int> CountTokensAsync(string text, LlmModel model, CancellationToken ct = default);
    Task<bool> IsWithinBudgetAsync(Guid agentId, int estimatedTokens, CancellationToken ct = default);
}
