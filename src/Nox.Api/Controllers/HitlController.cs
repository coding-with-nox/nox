using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Nox.Api.Auth;
using Nox.Api.Hubs;
using Nox.Domain;
using Nox.Domain.Hitl;
using Nox.Orleans.GrainInterfaces;
using Orleans;
using System.Text.Json.Nodes;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = NoxPolicies.AnyUser)]
public class HitlController(
    IHitlQueue hitlQueue,
    IClusterClient orleans,
    IHubContext<HitlHub> hubContext) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<IActionResult> GetAllPending([FromQuery] int skip = 0, [FromQuery] int take = 50)
        => Ok(await hitlQueue.GetAllPendingAsync(skip, take));

    [HttpGet("pending/{flowRunId:guid}")]
    public async Task<IActionResult> GetPendingByFlow(Guid flowRunId)
        => Ok(await hitlQueue.GetPendingByFlowAsync(flowRunId));

    [HttpGet("checkpoint/{id:guid}")]
    public async Task<IActionResult> GetCheckpoint(Guid id)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(id);
        return checkpoint is null ? NotFound() : Ok(checkpoint);
    }

    [HttpPost("checkpoint/{id:guid}/decide")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> SubmitDecision(Guid id, [FromBody] DecisionRequest req)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(id);
        if (checkpoint is null) return NotFound($"Checkpoint {id} not found or already resolved");

        // DecidedBy always comes from the authenticated JWT — never from request body
        var decidedBy = User.Identity?.Name
            ?? User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(decidedBy))
            return Unauthorized("Cannot determine authenticated user identity for HITL decision.");

        var decision = new HitlDecision
        {
            CheckpointId = id,
            Decision     = req.Decision,
            Payload      = req.Payload,
            DecidedBy    = decidedBy
        };

        await hitlQueue.SubmitDecisionAsync(id, decision);

        if (checkpoint.FlowRunId != Guid.Empty)
        {
            try
            {
                var flowGrain = orleans.GetGrain<IFlowGrain>(checkpoint.FlowRunId);
                await flowGrain.ResumeFromCheckpointAsync(id, decision);
            }
            catch (Exception ex)
            {
                HttpContext.RequestServices
                    .GetRequiredService<ILogger<HitlController>>()
                    .LogWarning(ex, "Could not resume flow grain {FlowRunId}", checkpoint.FlowRunId);
            }
        }

        await hubContext.Clients.All.SendAsync("CheckpointResolved", new
        {
            checkpointId = id,
            decision     = req.Decision,
            decidedBy    = decidedBy   // username from JWT, not from request body
        });

        return Ok(new { checkpointId = id, decision = req.Decision, status = "Resolved" });
    }

    [HttpPost("checkpoint/{id:guid}/escalate")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> Escalate(Guid id, [FromBody] EscalateRequest req)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(id);
        if (checkpoint is null) return NotFound();

        await hitlQueue.EscalateAsync(id, req.Reason ?? "Escalated by user");
        return Ok();
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] Guid? flowRunId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        var db = HttpContext.RequestServices
            .GetRequiredService<Nox.Infrastructure.Persistence.NoxDbContext>();

        var query = db.HitlCheckpoints
            .Where(h => h.Status != CheckpointStatus.Pending);

        if (flowRunId.HasValue)
            query = query.Where(h => h.FlowRunId == flowRunId.Value);

        var history = await query
            .OrderByDescending(h => h.ResolvedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(history);
    }
}

public record DecisionRequest(string Decision, JsonObject? Payload);
public record EscalateRequest(string? Reason);
