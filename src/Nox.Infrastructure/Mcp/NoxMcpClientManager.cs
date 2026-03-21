using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Hitl;
using Nox.Domain.Mcp;
using Nox.Infrastructure.Persistence;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Mcp;

public class NoxMcpClientManager(
    NoxDbContext db,
    IHitlQueue hitlQueue,
    ILogger<NoxMcpClientManager> logger) : IMcpClientManager
{
    public async Task<List<McpTool>> DiscoverToolsAsync(string serverId, CancellationToken ct = default)
    {
        var server = await db.McpServers.FindAsync([serverId], ct)
            ?? throw new KeyNotFoundException($"MCP server '{serverId}' not found");

        // TODO: connect to actual MCP server via SSE/Stdio and list tools
        // For now return empty list as stub
        logger.LogInformation("Discovering tools for MCP server {ServerId}", serverId);
        return [];
    }

    public async Task<JsonElement> InvokeToolAsync(string serverId, string toolName, JsonObject args, CancellationToken ct = default)
    {
        var server = await db.McpServers.FindAsync([serverId], ct)
            ?? throw new KeyNotFoundException($"MCP server '{serverId}' not found");

        logger.LogInformation("Invoking MCP tool {Tool} on server {Server}", toolName, serverId);

        // TODO: actual MCP client invocation
        return JsonSerializer.Deserialize<JsonElement>("{\"result\":\"stub\"}")!;
    }

    public async Task RequestNewServerAsync(Guid agentId, McpServerProposal proposal, CancellationToken ct = default)
    {
        var context = new JsonObject
        {
            ["name"] = proposal.Name,
            ["description"] = proposal.Description,
            ["transport"] = proposal.Transport.ToString(),
            ["endpointUrl"] = proposal.EndpointUrl,
            ["dockerImage"] = proposal.DockerImage,
            ["justification"] = proposal.Justification,
            ["proposedByAgentId"] = agentId.ToString()
        };

        var serverId = $"proposed-{Guid.NewGuid():N}";
        var serverInfo = new McpServerInfo
        {
            Id = serverId,
            Name = proposal.Name,
            Description = proposal.Description,
            Transport = proposal.Transport,
            EndpointUrl = proposal.EndpointUrl,
            DockerImage = proposal.DockerImage,
            EnvironmentVars = proposal.EnvironmentVars,
            Status = McpServerStatus.PendingApproval,
            ProposedByAgentId = agentId
        };

        db.McpServers.Add(serverInfo);
        await db.SaveChangesAsync(ct);

        await hitlQueue.EnqueueAsync(new HitlCheckpoint
        {
            FlowRunId = Guid.Empty,
            FlowNodeId = "mcp-server-request",
            Type = CheckpointType.Approval,
            Title = $"Approve MCP server: {proposal.Name}",
            Description = $"Agent {agentId} requests a new MCP server. Justification: {proposal.Justification}",
            Context = context
        }, ct);

        logger.LogInformation("Agent {AgentId} requested new MCP server '{Name}' — pending HITL", agentId, proposal.Name);
    }

    public async Task<List<McpServerInfo>> GetAvailableServersAsync(CancellationToken ct = default)
    {
        return await db.McpServers
            .Where(s => s.Status == McpServerStatus.Active)
            .ToListAsync(ct);
    }
}
