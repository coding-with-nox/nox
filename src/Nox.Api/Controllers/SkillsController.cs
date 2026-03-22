using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nox.Api.Auth;
using Nox.Domain;
using Nox.Domain.Skills;
using Nox.Infrastructure.Persistence;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = NoxPolicies.AnyUser)]
public class SkillsController(
    NoxDbContext db,
    ISkillRegistry skillRegistry) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? scope, [FromQuery] string? status)
    {
        var query = db.Skills.AsQueryable();
        if (scope is not null && Enum.TryParse<SkillScope>(scope, true, out var scopeEnum))
            query = query.Where(s => s.Scope == scopeEnum);
        if (status is not null && Enum.TryParse<SkillStatus>(status, true, out var statusEnum))
            query = query.Where(s => s.Status == statusEnum);
        return Ok(await query.OrderBy(s => s.Scope).ThenBy(s => s.Slug).ToListAsync());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var skill = await db.Skills.FindAsync(id);
        return skill is null ? NotFound() : Ok(skill);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
        => Ok(await db.Skills
            .Where(s => s.Status == SkillStatus.PendingApproval)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync());

    [HttpGet("slash-commands")]
    public async Task<IActionResult> GetSlashCommands([FromQuery] Guid? agentId)
        => Ok(await skillRegistry.GetSlashCommandsAsync(agentId ?? Guid.Empty));

    [HttpPost]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> Create([FromBody] Skill skill)
    {
        var registered = await skillRegistry.RegisterAsync(skill);
        return CreatedAtAction(nameof(Get), new { id = registered.Id }, registered);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var approvedBy = User.Identity?.Name
            ?? User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(approvedBy))
            return Unauthorized("Cannot determine authenticated user identity.");

        var skill = await skillRegistry.ApproveSkillAsync(id, approvedBy);
        return Ok(skill);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = NoxPolicies.ManagerOrAdmin)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectSkillRequest req)
    {
        var rejectedBy = User.Identity?.Name
            ?? User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst("email")?.Value;

        if (string.IsNullOrWhiteSpace(rejectedBy))
            return Unauthorized("Cannot determine authenticated user identity.");

        var skill = await skillRegistry.RejectSkillAsync(id, rejectedBy, req.Reason ?? "No reason given");
        return Ok(skill);
    }
}

public record RejectSkillRequest(string? Reason);
