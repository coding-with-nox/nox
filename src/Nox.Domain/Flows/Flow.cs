using System.Text.Json.Nodes;

namespace Nox.Domain.Flows;

public class Flow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required Guid ProjectId { get; init; }
    public FlowStatus Status { get; set; } = FlowStatus.Draft;
    public int Version { get; set; } = 1;
    public FlowGraph Graph { get; set; } = new();
    public JsonObject Variables { get; set; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedBy { get; init; }
    /// <summary>SHA-256 hex hash of the per-flow trigger key. Null = no trigger key configured.</summary>
    public string? TriggerKeyHash { get; set; }
}

public class FlowRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FlowId { get; init; }
    public FlowRunStatus Status { get; set; } = FlowRunStatus.Running;
    public JsonObject Variables { get; set; } = new();
    public List<string> CurrentNodeIds { get; set; } = [];
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}

public class FlowEvent
{
    public required Guid FlowRunId { get; init; }
    public required string EventType { get; init; }
    public required string NodeId { get; init; }
    public JsonObject Payload { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
