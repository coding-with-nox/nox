using System.Text.Json.Nodes;
using Nox.Domain.Agents;

namespace Nox.Domain.Messages;

public class AcpMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public AcpMessageType Type { get; init; } = AcpMessageType.Event;
    public AgentAddress? From { get; init; }
    public AgentAddress? To { get; init; }
    public required string Topic { get; init; }
    public JsonObject Payload { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan? Ttl { get; init; }
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
