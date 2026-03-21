using Nox.Domain;
using Nox.Domain.Agents;
using Nox.Domain.Memory;
using Nox.Domain.Messages;
using Nox.Domain.Skills;
using Orleans;

namespace Nox.Orleans.GrainInterfaces;

public interface IAgentGrain : IGrainWithGuidKey
{
    Task InitializeAsync(AgentInitRequest request);
    Task<TaskResult> ExecuteTaskAsync(TaskInput input, GrainCancellationToken ct);
    Task<AgentStatus> GetStatusAsync();
    Task SuspendAsync(string reason);
    Task ResumeAsync(Domain.Hitl.HitlDecision decision);
    Task TerminateAsync();
    Task<IAgentGrain> SpawnSubAgentAsync(Guid templateId, string? role = null);
    Task<int> GetSubAgentCountAsync();
    Task ReceiveMessageAsync(AcpMessage message);
    Task<List<MemoryChunk>> QueryMemoryAsync(string query, int topK = 5);
    Task<List<Skill>> GetEffectiveSkillsAsync();
    Task<AgentInfo> GetInfoAsync();
}

[GenerateSerializer]
public class AgentInfo
{
    [Id(0)] public Guid Id { get; init; }
    [Id(1)] public required string Name { get; init; }
    [Id(2)] public required string Role { get; init; }
    [Id(3)] public AgentStatus Status { get; init; }
    [Id(4)] public int TokensUsed { get; init; }
    [Id(5)] public int MaxSubAgents { get; init; }
    [Id(6)] public int CurrentSubAgentCount { get; init; }
    [Id(7)] public LlmModel Model { get; init; }
    [Id(8)] public Guid FlowRunId { get; init; }
    [Id(9)] public Guid? ParentAgentId { get; init; }
}
