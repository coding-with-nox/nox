using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nox.Api.Auth;
using Nox.Domain;
using Nox.Domain.Mcp;
using Nox.Infrastructure.Persistence;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/mcp")]
[Authorize(Policy = NoxPolicies.AnyUser)]
public class McpController(
    NoxDbContext db,
    IMcpClientManager mcpManager) : ControllerBase
{
    // GET /api/mcp/servers — list all servers
    [HttpGet("servers")]
    public async Task<IActionResult> ListServers([FromQuery] string? status)
    {
        var query = db.McpServers.AsQueryable();

        if (status is not null && Enum.TryParse<McpServerStatus>(status, true, out var statusEnum))
            query = query.Where(s => s.Status == statusEnum);

        var servers = await query.OrderBy(s => s.CreatedAt).ToListAsync();
        return Ok(servers);
    }

    // GET /api/mcp/servers/{id}
    [HttpGet("servers/{id}")]
    public async Task<IActionResult> GetServer(string id)
    {
        var server = await db.McpServers.FindAsync(id);
        return server is null ? NotFound() : Ok(server);
    }

    // GET /api/mcp/servers/{id}/tools — discover tools from a live server
    [HttpGet("servers/{id}/tools")]
    public async Task<IActionResult> DiscoverTools(string id, CancellationToken ct)
    {
        try
        {
            var tools = await mcpManager.DiscoverToolsAsync(id, ct);
            return Ok(tools);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // POST /api/mcp/servers — register a new server directly (admin)
    [HttpPost("servers")]
    public async Task<IActionResult> RegisterServer([FromBody] McpServerInfo server)
    {
        db.McpServers.Add(server);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetServer), new { id = server.Id }, server);
    }

    // POST /api/mcp/servers/{id}/propose — agent proposes a new MCP server (→ HITL)
    [HttpPost("servers/propose")]
    public async Task<IActionResult> ProposeServer([FromBody] McpServerProposeRequest req, CancellationToken ct)
    {
        await mcpManager.RequestNewServerAsync(req.AgentId, new McpServerProposal
        {
            Name = req.Name,
            Description = req.Description,
            Transport = req.Transport,
            EndpointUrl = req.EndpointUrl,
            DockerImage = req.DockerImage,
            EnvironmentVars = req.EnvironmentVars ?? new(),
            Justification = req.Justification
        }, ct);

        return Accepted(new { message = $"MCP server '{req.Name}' proposal submitted for HITL approval." });
    }

    // POST /api/mcp/servers/{id}/approve — approve a proposed server
    [HttpPost("servers/{id}/approve")]
    public async Task<IActionResult> ApproveServer(string id, [FromBody] ApproveMcpServerRequest req, CancellationToken ct)
    {
        try
        {
            var server = await mcpManager.ApproveServerAsync(id, req.ApprovedBy ?? User.Identity?.Name ?? "anonymous", ct);
            return Ok(server);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // POST /api/mcp/servers/{id}/invoke — invoke a tool directly (for testing)
    [HttpPost("servers/{id}/invoke")]
    public async Task<IActionResult> InvokeTool(string id, [FromBody] InvokeToolRequest req, CancellationToken ct)
    {
        try
        {
            var result = await mcpManager.InvokeToolAsync(id, req.ToolName, req.Args ?? new(), ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}

public record McpServerProposeRequest(
    Guid AgentId,
    string Name,
    string? Description,
    McpTransport Transport,
    string? EndpointUrl,
    string? DockerImage,
    System.Text.Json.Nodes.JsonObject? EnvironmentVars,
    string Justification);

public record ApproveMcpServerRequest(string? ApprovedBy);

public record InvokeToolRequest(string ToolName, System.Text.Json.Nodes.JsonObject? Args);
