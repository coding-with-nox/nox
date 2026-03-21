using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Llm;
using Nox.Domain.Memory;
using Nox.Infrastructure.Persistence;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Nox.Infrastructure.Memory;

public class QdrantMemoryStore(
    NoxDbContext db,
    QdrantClient qdrant,
    ILlmProvider llmProvider,
    ILogger<QdrantMemoryStore> logger) : IMemoryStore
{
    private const string CollectionName = "nox_project_memory";
    private const int VectorSize = 1536;

    public async Task<Guid> StoreAsync(MemoryEntry entry, CancellationToken ct = default)
    {
        await EnsureCollectionExistsAsync(ct);

        // Generate embedding
        var generator = llmProvider.GetEmbeddingGenerator();
        var embeddings = await generator.GenerateAsync([entry.Content], cancellationToken: ct);
        var vector = embeddings[0].Vector.ToArray();

        // Estimate token count
        entry.TokenCount = await llmProvider.CountTokensAsync(entry.Content, LlmModel.Claude4, ct);

        // Store in Qdrant
        var point = new PointStruct
        {
            Id = entry.QdrantPointId,
            Vectors = vector,
            Payload =
            {
                ["projectId"] = entry.ProjectId.ToString(),
                ["agentId"] = entry.AgentId?.ToString() ?? "",
                ["contentType"] = entry.ContentType.ToString(),
                ["importance"] = (double)entry.Importance,
                ["tokenCount"] = entry.TokenCount,
                ["postgresId"] = entry.Id.ToString(),
                ["createdAt"] = entry.CreatedAt.ToUnixTimeSeconds()
            }
        };

        foreach (var tag in entry.Tags)
            point.Payload[$"tag_{tag}"] = true;

        await qdrant.UpsertAsync(CollectionName, [point], cancellationToken: ct);

        // Store metadata in PostgreSQL
        db.ProjectMemory.Add(entry);
        await db.SaveChangesAsync(ct);

        logger.LogDebug("Memory entry {Id} stored for project {ProjectId}", entry.Id, entry.ProjectId);
        return entry.Id;
    }

    public async Task<List<MemoryChunk>> SearchAsync(Guid projectId, string query, int topK = 5,
        MemoryFilter? filter = null, CancellationToken ct = default)
    {
        await EnsureCollectionExistsAsync(ct);

        var generator = llmProvider.GetEmbeddingGenerator();
        var embeddings = await generator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();

        var qdrantFilter = BuildQdrantFilter(projectId, filter);

        var results = await qdrant.SearchAsync(
            collectionName: CollectionName,
            vector: queryVector,
            filter: qdrantFilter,
            limit: (ulong)topK,
            cancellationToken: ct);

        var chunks = new List<MemoryChunk>();
        foreach (var r in results)
        {
            if (!r.Payload.TryGetValue("postgresId", out var pgId)) continue;
            var id = Guid.Parse(pgId.StringValue);
            var entry = await db.ProjectMemory.FindAsync([id], ct);
            if (entry is null) continue;

            chunks.Add(new MemoryChunk
            {
                Id = entry.Id,
                Content = entry.Content,
                ContentType = entry.ContentType,
                Score = r.Score,
                TokenCount = entry.TokenCount,
                Tags = entry.Tags,
                Importance = entry.Importance,
                CreatedAt = entry.CreatedAt
            });
        }

        return chunks.OrderByDescending(c => c.Score).ToList();
    }

    public async Task<List<MemoryChunk>> GetByAgentAsync(Guid agentId, int limit = 20, CancellationToken ct = default)
    {
        var entries = await db.ProjectMemory
            .Where(m => m.AgentId == agentId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return entries.Select(e => new MemoryChunk
        {
            Id = e.Id,
            Content = e.Content,
            ContentType = e.ContentType,
            Score = e.Importance,
            TokenCount = e.TokenCount,
            Tags = e.Tags,
            Importance = e.Importance,
            CreatedAt = e.CreatedAt
        }).ToList();
    }

    public async Task SummarizeAndPruneAsync(Guid projectId, int targetTokenBudget, CancellationToken ct = default)
    {
        var current = await EstimateTokensAsync(projectId, null, ct);
        if (current <= targetTokenBudget) return;

        // Get lowest-importance entries first
        var toLow = await db.ProjectMemory
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Importance)
            .ThenBy(m => m.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        if (toLow.Count == 0) return;

        // Summarize via LLM
        var combined = string.Join("\n\n---\n\n", toLow.Select(m => m.Content));
        var chatClient = llmProvider.GetChatClient(LlmModel.Claude4);
        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, $"Summarize the following {toLow.Count} memory items into a single concise paragraph, preserving key decisions, code, and outcomes:\n\n{combined}")],
            cancellationToken: ct);

        var summary = response.Text ?? "Summary unavailable.";

        // Store summary
        await StoreAsync(new MemoryEntry
        {
            ProjectId = projectId,
            Content = summary,
            ContentType = MemoryContentType.Summary,
            Importance = 0.6f,
            Tags = ["auto-summary"]
        }, ct);

        // Delete originals
        foreach (var m in toLow)
        {
            await DeleteAsync(m.QdrantPointId, ct);
        }

        logger.LogInformation("Pruned {Count} memory entries for project {ProjectId}", toLow.Count, projectId);
    }

    public async Task DeleteAsync(Guid pointId, CancellationToken ct = default)
    {
        await qdrant.DeleteAsync(CollectionName, [pointId], cancellationToken: ct);

        var entry = await db.ProjectMemory.FirstOrDefaultAsync(m => m.QdrantPointId == pointId, ct);
        if (entry is not null)
        {
            db.ProjectMemory.Remove(entry);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> EstimateTokensAsync(Guid projectId, Guid? agentId = null, CancellationToken ct = default)
    {
        var query = db.ProjectMemory.Where(m => m.ProjectId == projectId);
        if (agentId.HasValue) query = query.Where(m => m.AgentId == agentId);
        return await query.SumAsync(m => m.TokenCount, ct);
    }

    private async Task EnsureCollectionExistsAsync(CancellationToken ct)
    {
        var collections = await qdrant.ListCollectionsAsync(ct);
        if (collections.Contains(CollectionName)) return;

        await qdrant.CreateCollectionAsync(CollectionName,
            new VectorParams
            {
                Size = VectorSize,
                Distance = Distance.Cosine
            }, cancellationToken: ct);

        logger.LogInformation("Qdrant collection '{Collection}' created", CollectionName);
    }

    private static Filter? BuildQdrantFilter(Guid projectId, MemoryFilter? filter)
    {
        var conditions = new List<Condition>
        {
            new()
            {
                Field = new FieldCondition
                {
                    Key = "projectId",
                    Match = new Match { Keyword = projectId.ToString() }
                }
            }
        };

        if (filter?.AgentId.HasValue == true)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "agentId",
                    Match = new Match { Keyword = filter.AgentId.Value.ToString() }
                }
            });
        }

        return new Filter { Must = { conditions } };
    }
}
