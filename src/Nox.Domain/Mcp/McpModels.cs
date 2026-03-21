using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Domain.Mcp;

public class McpServerInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public McpTransport Transport { get; init; } = McpTransport.Sse;
    public string? EndpointUrl { get; init; }
    public string? DockerImage { get; init; }
    public JsonObject EnvironmentVars { get; init; } = new();
    public McpServerStatus Status { get; set; } = McpServerStatus.Active;
    public Guid? ProposedByAgentId { get; init; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public class McpTool
{
    public required string ServerId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public JsonObject InputSchema { get; init; } = new();
}

public class McpToolRef
{
    public required string ServerId { get; init; }
    public required string ToolName { get; init; }
    public string? Description { get; init; }
}

public class McpServerProposal
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public McpTransport Transport { get; init; } = McpTransport.Sse;
    public string? EndpointUrl { get; init; }
    public string? DockerImage { get; init; }
    public JsonObject EnvironmentVars { get; init; } = new();
    public required string Justification { get; init; }
}

public interface IMcpClientManager
{
    Task<List<McpTool>> DiscoverToolsAsync(string serverId, CancellationToken ct = default);
    Task<JsonElement> InvokeToolAsync(string serverId, string toolName, JsonObject args, CancellationToken ct = default);
    Task RequestNewServerAsync(Guid agentId, McpServerProposal proposal, CancellationToken ct = default);
    Task<List<McpServerInfo>> GetAvailableServersAsync(CancellationToken ct = default);
}
