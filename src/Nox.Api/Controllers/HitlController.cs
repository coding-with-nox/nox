using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Nox.Api.Auth;
using Nox.Api.Hubs;
using Nox.Application.Commands;
using Nox.Application.Services;
using Nox.Domain;
using Nox.Domain.Hitl;
using System.Text.Json.Nodes;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = NoxPolicies.AnyUser)]
public class HitlController(
    IHitlApplicationService hitlService,
    IHitlQueue hitlQueue,
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
        var decidedBy = User.Identity?.Name
            ?? User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(decidedBy))
            return Unauthorized("Cannot determine authenticated user identity for HITL decision.");

        try
        {
            var resolved = await hitlService.SubmitDecisionAsync(
                new SubmitHitlDecisionCommand(id, req.Decision, req.Payload, decidedBy));

            await hubContext.Clients.All.SendAsync("CheckpointResolved", new
            {
                checkpointId = id,
                decision     = req.Decision,
                decidedBy
            });

            return Ok(new { checkpointId = id, decision = req.Decision, status = "Resolved" });
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("checkpoint/{id:guid}/escalate")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> Escalate(Guid id, [FromBody] EscalateRequest req)
    {
        try
        {
            await hitlService.EscalateAsync(new EscalateCheckpointCommand(id, req.Reason ?? "Escalated by user"));
            return Ok();
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] Guid? flowRunId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        var db = HttpContext.RequestServices
            .GetRequiredService<Nox.Infrastructure.Persistence.NoxDbContext>();

        var query = db.HitlCheckpoints.Where(h => h.Status != CheckpointStatus.Pending);
        if (flowRunId.HasValue)
            query = query.Where(h => h.FlowRunId == flowRunId.Value);

        return Ok(await query.OrderByDescending(h => h.ResolvedAt).Skip(skip).Take(take).ToListAsync());
    }
}

public record DecisionRequest(string Decision, JsonObject? Payload);
public record EscalateRequest(string? Reason);
