namespace Nox.Domain.Memory;

public interface IMemoryStore
{
    Task<Guid> StoreAsync(MemoryEntry entry, CancellationToken ct = default);
    Task<List<MemoryChunk>> SearchAsync(Guid projectId, string query, int topK = 5,
        MemoryFilter? filter = null, CancellationToken ct = default);
    Task<List<MemoryChunk>> GetByAgentAsync(Guid agentId, int limit = 20, CancellationToken ct = default);
    Task SummarizeAndPruneAsync(Guid projectId, int targetTokenBudget, CancellationToken ct = default);
    Task DeleteAsync(Guid pointId, CancellationToken ct = default);
    Task<int> EstimateTokensAsync(Guid projectId, Guid? agentId = null, CancellationToken ct = default);
}
