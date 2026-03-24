using System.Text.Json.Nodes;
using Orleans;

namespace Nox.Domain.Hitl;

public class HitlCheckpoint
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FlowRunId { get; init; }
    public required string FlowNodeId { get; init; }
    public CheckpointType Type { get; init; } = CheckpointType.Approval;
    public CheckpointStatus Status { get; set; } = CheckpointStatus.Pending;
    public required string Title { get; init; }
    public string? Description { get; init; }
    public JsonObject Context { get; init; } = new();
    public List<string>? DecisionOptions { get; init; }
    public string? Decision { get; set; }
    public JsonObject? DecisionPayload { get; set; }
    public string? DecisionBy { get; set; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow > ExpiresAt.Value;
}

[GenerateSerializer]
public class HitlDecision
{
    [Id(0)] public required Guid CheckpointId { get; init; }
    [Id(1)] public required string Decision { get; init; }
    [Id(2)] public JsonObject? Payload { get; init; }
    [Id(3)] public required string DecidedBy { get; init; }
    [Id(4)] public DateTimeOffset DecidedAt { get; init; } = DateTimeOffset.UtcNow;
}
