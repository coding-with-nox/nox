using ModelContextProtocol.Server;
using Nox.Domain;
using Nox.Domain.Memory;
using System.ComponentModel;
using System.Text.Json;

namespace Nox.McpServer.Tools;

[McpServerToolType]
public static class MemoryTools
{
    [McpServerTool]
    [Description("Search project memory for relevant context using semantic similarity")]
    public static async Task<string> SearchMemory(
        [Description("Project ID")] string projectId,
        [Description("Search query")] string query,
        [Description("Number of results (default 5)")] int topK,
        IMemoryStore memoryStore)
    {
        var chunks = await memoryStore.SearchAsync(Guid.Parse(projectId), query, topK);
        return JsonSerializer.Serialize(chunks.Select(c => new
        {
            id = c.Id,
            content = c.Content,
            contentType = c.ContentType.ToString(),
            score = c.Score,
            tokenCount = c.TokenCount,
            tags = c.Tags,
            importance = c.Importance,
            createdAt = c.CreatedAt
        }));
    }

    [McpServerTool]
    [Description("Store a new memory entry in the project memory")]
    public static async Task<string> StoreMemory(
        [Description("Project ID")] string projectId,
        [Description("Content to store")] string content,
        [Description("Content type: Code, Design, Decision, Error, Summary, Requirement")] string contentType,
        [Description("Comma-separated tags")] string tags,
        [Description("Importance 0.0-1.0 (default 0.5)")] float importance,
        IMemoryStore memoryStore)
    {
        var id = await memoryStore.StoreAsync(new MemoryEntry
        {
            ProjectId = Guid.Parse(projectId),
            Content = content,
            ContentType = Enum.Parse<MemoryContentType>(contentType, ignoreCase: true),
            Tags = tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList(),
            Importance = Math.Clamp(importance, 0f, 1f)
        });

        return $"Memory entry {id} stored.";
    }

    [McpServerTool]
    [Description("Estimate total token usage for a project's memory")]
    public static async Task<string> EstimateMemoryTokens(
        [Description("Project ID")] string projectId,
        IMemoryStore memoryStore)
    {
        var tokens = await memoryStore.EstimateTokensAsync(Guid.Parse(projectId));
        return JsonSerializer.Serialize(new { projectId, estimatedTokens = tokens });
    }
}
