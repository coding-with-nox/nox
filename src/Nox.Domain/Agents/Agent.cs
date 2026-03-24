using System.Text.Json.Nodes;
using Orleans;

namespace Nox.Domain.Agents;

public class TokenBudgetConfig
{
    public int TotalBudget { get; init; } = 128_000;
    public int WorkingMemoryReserve { get; init; } = 8_000;
    public int MemoryContextBudget { get; init; } = 20_000;
    public int OutputReserve { get; init; } = 4_000;
    public int SkillContextBudget { get; init; } = 2_000;
}

public class AgentTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Role { get; set; }
    public string? Description { get; set; }
    public LlmModel DefaultModel { get; set; } = LlmModel.Claude4;
    public string SystemPromptTemplate { get; set; } = string.Empty;
    public int DefaultMaxSubAgents { get; set; } = 3;
    public List<string> SkillGroups { get; set; } = [];
    public List<string> DefaultMcpServers { get; set; } = [];
    public TokenBudgetConfig TokenBudgetConfig { get; set; } = new();
    public bool IsGlobal { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Agent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid TemplateId { get; init; }
    public required Guid FlowRunId { get; init; }
    public Guid? ParentAgentId { get; init; }
    public required string Name { get; set; }
    public required string Role { get; set; }
    public LlmModel Model { get; set; } = LlmModel.Claude4;
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public int MaxSubAgents { get; set; } = 3;
    public int CurrentSubAgentCount { get; set; }
    public int TokensUsed { get; set; }
    public List<string> McpServerBindings { get; set; } = [];
    public JsonObject Metadata { get; set; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public record AgentAddress([property: Id(0)] Guid AgentId, [property: Id(1)] Guid FlowRunId);

[GenerateSerializer]
public class TaskInput
{
    [Id(0)] public required string FlowNodeId { get; init; }
    [Id(1)] public required Guid FlowRunId { get; init; }
    [Id(2)] public JsonObject Payload { get; init; } = new();
    [Id(3)] public string? ParentTaskId { get; init; }
}

[GenerateSerializer]
public class TaskResult
{
    [Id(0)] public required Guid TaskId { get; init; }
    [Id(1)] public required bool Success { get; init; }
    [Id(2)] public JsonObject Output { get; init; } = new();
    [Id(3)] public int TokensUsed { get; init; }
    [Id(4)] public string? Error { get; init; }
}

[GenerateSerializer]
public class AgentInitRequest
{
    [Id(0)] public required Guid TemplateId { get; init; }
    [Id(1)] public required Guid FlowRunId { get; init; }
    [Id(2)] public required Guid ProjectId { get; init; }
    [Id(3)] public Guid? ParentAgentId { get; init; }
    [Id(4)] public int MaxSubAgents { get; init; } = 3;
}
