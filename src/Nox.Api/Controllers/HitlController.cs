using Nox.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Nox.Api.Hubs;
using Nox.Domain.Hitl;
using Nox.Orleans.GrainInterfaces;
using Orleans;
using System.Text.Json.Nodes;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HitlController(
    IHitlQueue hitlQueue,
    IClusterClient orleans,
    IHubContext<HitlHub> hubContext) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<IActionResult> GetAllPending([FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var checkpoints = await hitlQueue.GetAllPendingAsync(skip, take);
        return Ok(checkpoints);
    }

    [HttpGet("pending/{flowRunId:guid}")]
    public async Task<IActionResult> GetPendingByFlow(Guid flowRunId)
    {
        var checkpoints = await hitlQueue.GetPendingByFlowAsync(flowRunId);
        return Ok(checkpoints);
    }

    [HttpGet("checkpoint/{id:guid}")]
    public async Task<IActionResult> GetCheckpoint(Guid id)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(id);
        return checkpoint is null ? NotFound() : Ok(checkpoint);
    }

    [HttpPost("checkpoint/{id:guid}/decide")]
    public async Task<IActionResult> SubmitDecision(Guid id, [FromBody] DecisionRequest req)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(id);
        if (checkpoint is null) return NotFound($"Checkpoint {id} not found or already resolved");

        var decision = new HitlDecision
        {
            CheckpointId = id,
            Decision = req.Decision,
            Payload = req.Payload,
            DecidedBy = req.DecidedBy ?? User.Identity?.Name ?? "anonymous"
        };

        await hitlQueue.SubmitDecisionAsync(id, decision);

        // Resume the flow grain if applicable
        if (checkpoint.FlowRunId != Guid.Empty)
        {
            try
            {
                var flowGrain = orleans.GetGrain<IFlowGrain>(checkpoint.FlowRunId);
                await flowGrain.ResumeFromCheckpointAsync(id, decision);
            }
            catch (Exception ex)
            {
                // Log but don't fail — decision was stored
                HttpContext.RequestServices
                    .GetRequiredService<ILogger<HitlController>>()
                    .LogWarning(ex, "Could not resume flow grain {FlowRunId}", checkpoint.FlowRunId);
            }
        }

        // Notify dashboard via SignalR
        await hubContext.Clients.All.SendAsync("CheckpointResolved", new
        {
            checkpointId = id,
            decision = req.Decision,
            decidedBy = decision.DecidedBy
        });

        return Ok(new { checkpointId = id, decision = req.Decision, status = "Resolved" });
    }

    [HttpPost("checkpoint/{id:guid}/escalate")]
    public async Task<IActionResult> Escalate(Guid id, [FromBody] EscalateRequest req)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(id);
        if (checkpoint is null) return NotFound();

        await hitlQueue.EscalateAsync(id, req.Reason ?? "Escalated by user");
        return Ok();
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] Guid? flowRunId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        // Returns resolved checkpoints
        var query = HttpContext.RequestServices
            .GetRequiredService<Nox.Infrastructure.Persistence.NoxDbContext>()
            .HitlCheckpoints
            .Where(h => h.Status != CheckpointStatus.Pending);

        if (flowRunId.HasValue) query = query.Where(h => h.FlowRunId == flowRunId.Value);

        var history = await query
            .OrderByDescending(h => h.ResolvedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(history);
    }
}

public record DecisionRequest(string Decision, string? DecidedBy, JsonObject? Payload);
public record EscalateRequest(string? Reason);
