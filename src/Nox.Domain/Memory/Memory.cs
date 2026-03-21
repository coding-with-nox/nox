namespace Nox.Domain.Memory;

public class MemoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ProjectId { get; init; }
    public Guid? AgentId { get; init; }
    public required string Content { get; init; }
    public MemoryContentType ContentType { get; init; } = MemoryContentType.Summary;
    public Guid QdrantPointId { get; init; } = Guid.NewGuid();
    public int TokenCount { get; set; }
    public List<string> Tags { get; init; } = [];
    public float Importance { get; set; } = 0.5f;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class MemoryChunk
{
    public required Guid Id { get; init; }
    public required string Content { get; init; }
    public MemoryContentType ContentType { get; init; }
    public float Score { get; init; }
    public int TokenCount { get; init; }
    public List<string> Tags { get; init; } = [];
    public float Importance { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public class MemoryFilter
{
    public Guid? AgentId { get; init; }
    public MemoryContentType? ContentType { get; init; }
    public List<string>? Tags { get; init; }
    public float? MinImportance { get; init; }
}
