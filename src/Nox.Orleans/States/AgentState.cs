using Nox.Domain;
using Nox.Domain.Agents;
using Orleans;

namespace Nox.Orleans.States;

[GenerateSerializer]
public class AgentState
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid TemplateId { get; set; }
    [Id(2)] public Guid FlowRunId { get; set; }
    [Id(3)] public Guid ProjectId { get; set; }
    [Id(4)] public Guid? ParentAgentId { get; set; }
    [Id(5)] public string Name { get; set; } = string.Empty;
    [Id(6)] public string Role { get; set; } = string.Empty;
    [Id(7)] public string SystemPrompt { get; set; } = string.Empty;
    [Id(8)] public LlmModel Model { get; set; } = LlmModel.Claude4;
    [Id(9)] public AgentStatus Status { get; set; } = AgentStatus.Idle;
    [Id(10)] public int MaxSubAgents { get; set; } = 3;
    [Id(11)] public int CurrentSubAgentCount { get; set; }
    [Id(12)] public int TokensUsed { get; set; }
    [Id(13)] public List<Guid> SubAgentIds { get; set; } = [];
    [Id(14)] public List<string> McpServerBindings { get; set; } = [];
    [Id(15)] public bool IsInitialized { get; set; }
    [Id(16)] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
