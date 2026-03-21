using Nox.Domain;
using Nox.Domain.Flows;
using Orleans;

namespace Nox.Orleans.States;

[GenerateSerializer]
public class FlowState
{
    [Id(0)] public Guid FlowRunId { get; set; }
    [Id(1)] public Guid FlowId { get; set; }
    [Id(2)] public Guid ProjectId { get; set; }
    [Id(3)] public FlowRunStatus Status { get; set; } = FlowRunStatus.Running;
    [Id(4)] public List<string> CurrentNodeIds { get; set; } = [];
    [Id(5)] public List<Guid> PendingCheckpointIds { get; set; } = [];
    [Id(6)] public Dictionary<string, string> NodeResults { get; set; } = new();
    [Id(7)] public string Variables { get; set; } = "{}";
    [Id(8)] public string FlowGraphJson { get; set; } = "{}";
    [Id(9)] public int ForkJoinCounter { get; set; }
    [Id(10)] public List<Guid> SpawnedAgentIds { get; set; } = [];
    [Id(11)] public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    [Id(12)] public DateTimeOffset? CompletedAt { get; set; }
    [Id(13)] public string? Error { get; set; }
}
