using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nox.Domain.Flows;
using Nox.Infrastructure.Persistence;
using Nox.Orleans.GrainInterfaces;
using Orleans;
using System.Text.Json.Nodes;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FlowsController(
    NoxDbContext db,
    IFlowEngine flowEngine,
    IClusterClient orleans) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? projectId)
    {
        var query = db.Flows.AsQueryable();
        if (projectId.HasValue) query = query.Where(f => f.ProjectId == projectId.Value);
        var flows = await query.OrderByDescending(f => f.UpdatedAt).ToListAsync();
        return Ok(flows);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var flow = await db.Flows.FindAsync(id);
        return flow is null ? NotFound() : Ok(flow);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFlowRequest req)
    {
        var flow = new Flow
        {
            Name = req.Name,
            Description = req.Description,
            ProjectId = req.ProjectId,
            Graph = req.Graph ?? new FlowGraph(),
            CreatedBy = req.CreatedBy ?? "system"
        };
        db.Flows.Add(flow);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = flow.Id }, flow);
    }

    [HttpPut("{id:guid}")]
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

    // --- Flow Runs ---

    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> ListRuns(Guid id)
    {
        var runs = await db.FlowRuns.Where(r => r.FlowId == id)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync();
        return Ok(runs);
    }

    [HttpPost("{id:guid}/runs")]
    public async Task<IActionResult> StartRun(Guid id, [FromBody] StartFlowRunRequest req)
    {
        var flow = await db.Flows.FindAsync(id);
        if (flow is null) return NotFound($"Flow {id} not found");

        var run = await flowEngine.StartAsync(id, req.Variables);
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
    public async Task<IActionResult> CancelRun(Guid runId, [FromBody] CancelRunRequest req)
    {
        var grain = orleans.GetGrain<IFlowGrain>(runId);
        await grain.CancelAsync(req.Reason ?? "Cancelled by user");
        return Ok();
    }
}

public record CreateFlowRequest(string Name, string? Description, Guid ProjectId, FlowGraph? Graph, string? CreatedBy);
public record UpdateFlowRequest(string? Name, string? Description, FlowGraph? Graph);
public record StartFlowRunRequest(JsonObject? Variables);
public record CancelRunRequest(string? Reason);
