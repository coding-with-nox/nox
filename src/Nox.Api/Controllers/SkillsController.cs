using Nox.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nox.Domain.Skills;
using Nox.Infrastructure.Persistence;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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

        var skills = await query.OrderBy(s => s.Scope).ThenBy(s => s.Slug).ToListAsync();
        return Ok(skills);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var skill = await db.Skills.FindAsync(id);
        return skill is null ? NotFound() : Ok(skill);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Skill skill)
    {
        var registered = await skillRegistry.RegisterAsync(skill);
        return CreatedAtAction(nameof(Get), new { id = registered.Id }, registered);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveSkillRequest req)
    {
        var skill = await skillRegistry.ApproveSkillAsync(id, req.ApprovedBy ?? User.Identity?.Name ?? "anonymous");
        return Ok(skill);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectSkillRequest req)
    {
        var skill = await skillRegistry.RejectSkillAsync(id,
            req.RejectedBy ?? User.Identity?.Name ?? "anonymous",
            req.Reason ?? "No reason given");
        return Ok(skill);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var pending = await db.Skills
            .Where(s => s.Status == SkillStatus.PendingApproval)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
        return Ok(pending);
    }

    [HttpGet("slash-commands")]
    public async Task<IActionResult> GetSlashCommands([FromQuery] Guid? agentId)
    {
        var commands = await skillRegistry.GetSlashCommandsAsync(agentId ?? Guid.Empty);
        return Ok(commands);
    }
}

public record ApproveSkillRequest(string? ApprovedBy);
public record RejectSkillRequest(string? RejectedBy, string? Reason);
