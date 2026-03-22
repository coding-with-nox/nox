using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Nox.Domain;
using Nox.Domain.Hitl;
using Nox.Domain.Mcp;
using Nox.Infrastructure.Persistence;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Mcp;

/// <summary>
/// MCP client manager — connects to external MCP servers via HTTP JSON-RPC (SSE transport).
/// Maintains a per-session connection cache. Implements tools/list and tools/call.
/// </summary>
public class NoxMcpClientManager(
    NoxDbContext db,
    IHitlQueue hitlQueue,
    IHttpClientFactory httpClientFactory,
    ILogger<NoxMcpClientManager> logger) : IMcpClientManager
{
    private static int _requestId = 0;

    public async Task<List<McpTool>> DiscoverToolsAsync(string serverId, CancellationToken ct = default)
    {
        var server = await db.McpServers.FindAsync([serverId], ct)
            ?? throw new KeyNotFoundException($"MCP server '{serverId}' not found");

        if (server.Status != McpServerStatus.Active)
            throw new InvalidOperationException($"MCP server '{serverId}' is not active (status: {server.Status})");

        if (server.Transport == McpTransport.Stdio)
            throw new NotSupportedException("Stdio MCP transport not supported in this host — use SSE or HTTP.");

        var endpoint = server.EndpointUrl
            ?? throw new InvalidOperationException($"MCP server '{serverId}' has no endpoint URL");

        logger.LogInformation("Discovering tools for MCP server {ServerId} at {Endpoint}", serverId, endpoint);

        try
        {
            var response = await SendJsonRpcAsync(endpoint, "tools/list", new JsonObject(), ct);
            var tools = new List<McpTool>();

            if (response.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsArray)
            {
                foreach (var item in toolsArray)
                {
                    if (item is not JsonObject toolObj) continue;
                    tools.Add(new McpTool
                    {
                        Name = toolObj["name"]?.GetValue<string>() ?? string.Empty,
                        Description = toolObj["description"]?.GetValue<string>(),
                        InputSchema = toolObj["inputSchema"]?.ToJsonString()
                    });
                }
            }

            logger.LogInformation("Discovered {Count} tools from server {ServerId}", tools.Count, serverId);
            return tools;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover tools from MCP server {ServerId}", serverId);
            return [];
        }
    }

    public async Task<JsonElement> InvokeToolAsync(string serverId, string toolName, JsonObject args, CancellationToken ct = default)
    {
        var server = await db.McpServers.FindAsync([serverId], ct)
            ?? throw new KeyNotFoundException($"MCP server '{serverId}' not found");

        if (server.Status != McpServerStatus.Active)
            throw new InvalidOperationException($"MCP server '{serverId}' is not active");

        var endpoint = server.EndpointUrl
            ?? throw new InvalidOperationException($"MCP server '{serverId}' has no endpoint URL");

        logger.LogInformation("Invoking MCP tool {Tool} on server {Server}", toolName, serverId);

        var result = await SendJsonRpcAsync(endpoint, "tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = args.DeepClone()
        }, ct);

        // Extract text from content blocks
        if (result.TryGetPropertyValue("content", out var contentNode) && contentNode is JsonArray contentArray)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var block in contentArray)
            {
                if (block is JsonObject blockObj &&
                    blockObj["type"]?.GetValue<string>() == "text")
                    sb.AppendLine(blockObj["text"]?.GetValue<string>());
            }
            var combinedText = sb.ToString().TrimEnd();
            return JsonDocument.Parse($"\"{JsonEncodedText.Encode(combinedText)}\"").RootElement;
        }

        return JsonDocument.Parse(result.ToJsonString()).RootElement;
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

    public async Task<McpServerInfo> ApproveServerAsync(string serverId, string approvedBy, CancellationToken ct = default)
    {
        var server = await db.McpServers.FindAsync([serverId], ct)
            ?? throw new KeyNotFoundException($"MCP server '{serverId}' not found");

        server.Status = McpServerStatus.Active;
        server.ApprovedBy = approvedBy;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("MCP server '{ServerId}' approved by {By}", serverId, approvedBy);
        return server;
    }

    private async Task<JsonObject> SendJsonRpcAsync(string endpoint, string method, JsonObject parameters, CancellationToken ct)
    {
        var id = System.Threading.Interlocked.Increment(ref _requestId);
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };

        var client = httpClientFactory.CreateClient("McpClient");
        var content = new StringContent(request.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

        // MCP HTTP transport: POST to /rpc or the endpoint directly
        var url = endpoint.EndsWith("/rpc") ? endpoint : endpoint.TrimEnd('/') + "/rpc";
        var httpResponse = await client.PostAsync(url, content, ct);
        httpResponse.EnsureSuccessStatusCode();

        var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);
        var responseDoc = JsonNode.Parse(responseJson)?.AsObject()
            ?? throw new InvalidOperationException("Invalid JSON-RPC response");

        if (responseDoc.TryGetPropertyValue("error", out var errorNode) && errorNode is not null)
        {
            var errorMsg = errorNode["message"]?.GetValue<string>() ?? "Unknown MCP error";
            throw new InvalidOperationException($"MCP error: {errorMsg}");
        }

        return responseDoc["result"]?.AsObject() ?? new JsonObject();
    }
}
