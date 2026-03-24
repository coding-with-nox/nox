using System.Text.Json.Nodes;
using Nox.Domain.Agents;
using Orleans;

namespace Nox.Domain.Messages;

[GenerateSerializer]
public class AcpMessage
{
    [Id(0)] public Guid Id { get; init; } = Guid.NewGuid();
    [Id(1)] public Guid CorrelationId { get; init; } = Guid.NewGuid();
    [Id(2)] public AcpMessageType Type { get; init; } = AcpMessageType.Event;
    [Id(3)] public AgentAddress? From { get; init; }
    [Id(4)] public AgentAddress? To { get; init; }
    [Id(5)] public required string Topic { get; init; }
    [Id(6)] public JsonObject Payload { get; init; } = new();
    [Id(7)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    [Id(8)] public TimeSpan? Ttl { get; init; }
}

public static class AcpTopics
{
    public const string TaskAssigned = "task.assigned";
    public const string TaskResult = "task.result";
    public const string TaskProgress = "task.progress";
    public const string AgentSpawnRequest = "agent.spawn.request";
    public const string AgentSpawnResponse = "agent.spawn.response";
    public const string SkillPropose = "skill.propose";
    public const string SkillApproved = "skill.approved";
    public const string SkillRejected = "skill.rejected";
    public const string HitlRequest = "hitl.request";
    public const string HitlDecision = "hitl.decision";
    public const string MemoryStore = "memory.store";
    public const string MemoryQueryRequest = "memory.query.request";
    public const string MemoryQueryResponse = "memory.query.response";
    public const string McpServerRequest = "mcp.server.request";
    public const string Broadcast = "broadcast";
}
