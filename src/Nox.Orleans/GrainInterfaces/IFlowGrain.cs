using Nox.Domain;
using Nox.Domain.Agents;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using Orleans;
using System.Text.Json.Nodes;

namespace Nox.Orleans.GrainInterfaces;

public interface IFlowGrain : IGrainWithGuidKey
{
    Task<FlowRunState> StartAsync(FlowStartRequest request);
    Task AdvanceAsync(string completedNodeId, TaskResult result);
    Task PauseAtCheckpointAsync(Guid checkpointId);
    Task ResumeFromCheckpointAsync(Guid checkpointId, HitlDecision decision);
    Task<FlowRunState> GetStateAsync();
    Task<List<string>> GetReadyNodeIdsAsync();
    Task CancelAsync(string reason);
}

[GenerateSerializer]
public class FlowStartRequest
{
    [Id(0)] public required Guid FlowId { get; init; }
    [Id(1)] public required Guid ProjectId { get; init; }
    [Id(2)] public string Variables { get; init; } = "{}";
}

[GenerateSerializer]
public class FlowRunState
{
    [Id(0)] public Guid FlowRunId { get; init; }
    [Id(1)] public Guid FlowId { get; init; }
    [Id(2)] public FlowRunStatus Status { get; init; }
    [Id(3)] public List<string> CurrentNodeIds { get; init; } = [];
    [Id(4)] public List<Guid> PendingCheckpointIds { get; init; } = [];
    [Id(5)] public string Variables { get; init; } = "{}";
    [Id(6)] public DateTimeOffset StartedAt { get; init; }
    [Id(7)] public DateTimeOffset? CompletedAt { get; init; }
    [Id(8)] public string? Error { get; init; }
}
