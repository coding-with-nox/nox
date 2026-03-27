using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nox.Api.Auth;
using Nox.Application.Commands;
using Nox.Application.Services;
using Nox.Domain.Flows;
using Nox.Infrastructure.Persistence;
using Nox.Orleans.GrainInterfaces;
using Orleans;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = NoxPolicies.AnyUser)]
public class FlowsController(
    NoxDbContext db,
    IFlowApplicationService flowService,
    IClusterClient orleans) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? projectId)
    {
        var query = db.Flows.AsQueryable();
        if (projectId.HasValue) query = query.Where(f => f.ProjectId == projectId.Value);
        return Ok(await query.OrderByDescending(f => f.UpdatedAt).ToListAsync());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var flow = await db.Flows.FindAsync(id);
        return flow is null ? NotFound() : Ok(flow);
    }

    [HttpPost]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> Create([FromBody] CreateFlowRequest req)
    {
        var flow = new Flow
        {
            Name = req.Name,
            Description = req.Description,
            ProjectId = req.ProjectId,
            Graph = req.Graph ?? new FlowGraph(),
            CreatedBy = User.Identity?.Name ?? User.FindFirst("preferred_username")?.Value ?? "unknown"
        };
        db.Flows.Add(flow);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = flow.Id }, flow);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFlowRequest req)
    {
        var flow = await db.Flows.FindAsync(id);
        if (flow is null) return NotFound();

        if (req.Name is not null) flow.Name = req.Name;
        if (req.Description is not null) flow.Description = req.Description;
        if (req.Graph is not null) { flow.Graph = req.Graph; flow.Version++; }
        flow.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Ok(flow);
    }

    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> ListRuns(Guid id)
    {
        var runs = await db.FlowRuns.Where(r => r.FlowId == id)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync();
        return Ok(runs);
    }

    [HttpPost("{id:guid}/runs")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> StartRun(Guid id, [FromBody] StartFlowRunRequest req)
    {
        var flow = await db.Flows.FindAsync(id);
        if (flow is null) return NotFound($"Flow {id} not found");

        if (!flow.Graph.Nodes.Any(n => n.NodeType == Nox.Domain.NodeType.Start))
            return BadRequest("Il grafo del flow non ha un nodo Start. Crea un nuovo flow dal designer.");

        var run = await flowService.StartRunAsync(new StartFlowRunCommand(id, req.Variables));
        return CreatedAtAction(nameof(GetRun), new { runId = run.Id }, run);
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> GetRun(Guid runId)
    {
        var grain = orleans.GetGrain<IFlowGrain>(runId);
        var state = await grain.GetStateAsync();
        return Ok(state);
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> CancelRun(Guid runId, [FromBody] CancelRunRequest req)
    {
        await flowService.CancelRunAsync(new CancelFlowRunCommand(runId, req.Reason ?? "Cancelled by user"));
        return Ok();
    }

    /// <summary>
    /// Genera (o rigenera) il trigger key per un flow.
    /// Restituisce la chiave in chiaro UNA SOLA VOLTA — viene salvato solo l'hash SHA-256.
    /// </summary>
    [HttpPost("{id:guid}/trigger-key")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> GenerateTriggerKey(Guid id)
    {
        var flow = await db.Flows.FindAsync(id);
        if (flow is null) return NotFound();

        var plainKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                              .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        flow.TriggerKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainKey)));
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { triggerKey = plainKey, flowId = id, note = "Salva questa chiave: non sarà mostrata di nuovo." });
    }

    /// <summary>
    /// Endpoint pubblico per avviare un flow tramite trigger key.
    /// Autenticato con header X-Flow-Trigger-Key (chiave in chiaro).
    /// </summary>
    [HttpPost("{id:guid}/trigger")]
    [AllowAnonymous]
    public async Task<IActionResult> TriggerRun(Guid id, [FromBody] StartFlowRunRequest req)
    {
        var flow = await db.Flows.FindAsync(id);
        if (flow is null) return NotFound();

        if (string.IsNullOrEmpty(flow.TriggerKeyHash))
            return Unauthorized(new { error = "Nessun trigger key configurato per questo flow." });

        if (!Request.Headers.TryGetValue("X-Flow-Trigger-Key", out var providedKey) || string.IsNullOrEmpty(providedKey))
            return Unauthorized(new { error = "Header X-Flow-Trigger-Key mancante." });

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providedKey.ToString())));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hash),
                Encoding.UTF8.GetBytes(flow.TriggerKeyHash)))
            return Unauthorized(new { error = "Trigger key non valido." });

        if (!flow.Graph.Nodes.Any(n => n.NodeType == Nox.Domain.NodeType.Start))
            return BadRequest("Il grafo del flow non ha un nodo Start.");

        var run = await flowService.StartRunAsync(new StartFlowRunCommand(id, req.Variables));
        return CreatedAtAction(nameof(GetRun), new { runId = run.Id }, run);
    }
}

public record CreateFlowRequest(string Name, string? Description, Guid ProjectId, FlowGraph? Graph, string? CreatedBy);
public record UpdateFlowRequest(string? Name, string? Description, FlowGraph? Graph);
public record StartFlowRunRequest(JsonObject? Variables);
public record CancelRunRequest(string? Reason);
