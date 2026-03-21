using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nox.Domain.Agents;
using Nox.Domain.Skills;
using Nox.Infrastructure.Persistence;
using Nox.Orleans.GrainInterfaces;
using Orleans;

namespace Nox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentsController(
    NoxDbContext db,
    ISkillRegistry skillRegistry,
    IClusterClient orleans) : ControllerBase
{
    // --- Templates ---

    [HttpGet("templates")]
    public async Task<IActionResult> ListTemplates()
    {
        var templates = await db.AgentTemplates.ToListAsync();
        return Ok(templates);
    }

    [HttpGet("templates/{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id)
    {
        var t = await db.AgentTemplates.FindAsync(id);
        return t is null ? NotFound() : Ok(t);
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] AgentTemplate template)
    {
        db.AgentTemplates.Add(template);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, template);
    }

    [HttpPut("templates/{id:guid}")]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] AgentTemplate updated)
    {
        var template = await db.AgentTemplates.FindAsync(id);
        if (template is null) return NotFound();

        template.Name = updated.Name;
        template.Role = updated.Role;
        template.Description = updated.Description;
        template.DefaultModel = updated.DefaultModel;
        template.SystemPromptTemplate = updated.SystemPromptTemplate;
        template.DefaultMaxSubAgents = updated.DefaultMaxSubAgents;
        template.SkillGroups = updated.SkillGroups;
        template.DefaultMcpServers = updated.DefaultMcpServers;
        template.TokenBudgetConfig = updated.TokenBudgetConfig;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Ok(template);
    }

    // --- Running agents ---

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> GetAgentsByRun(Guid runId)
    {
        var agents = await db.Agents
            .Where(a => a.FlowRunId == runId)
            .ToListAsync();
        return Ok(agents);
    }

    [HttpGet("{id:guid}/info")]
    public async Task<IActionResult> GetAgentInfo(Guid id)
    {
        var grain = orleans.GetGrain<IAgentGrain>(id);
        var info = await grain.GetInfoAsync();
        return Ok(info);
    }

    [HttpGet("{id:guid}/skills")]
    public async Task<IActionResult> GetAgentSkills(Guid id)
    {
        var skills = await skillRegistry.GetEffectiveSkillsAsync(id);
        return Ok(skills);
    }

    [HttpPost("{id:guid}/terminate")]
    public async Task<IActionResult> Terminate(Guid id)
    {
        var grain = orleans.GetGrain<IAgentGrain>(id);
        await grain.TerminateAsync();
        return Ok();
    }
}
