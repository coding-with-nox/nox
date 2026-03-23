using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Hitl;
using Nox.Domain.Skills;
using Nox.Infrastructure.Persistence;
using System.Text.Json.Nodes;

namespace Nox.Infrastructure.Skills;

public class PostgresSkillRegistry(
    NoxDbContext db,
    IHitlQueue hitlQueue,
    IMemoryCache cache,
    ILogger<PostgresSkillRegistry> logger) : ISkillRegistry
{
    private const string CacheKeyPrefix = "skills:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<Skill?> ResolveAsync(string slug, Guid agentId, string? groupId = null, CancellationToken ct = default)
    {
        var skills = await GetEffectiveSkillsAsync(agentId, groupId, ct);
        return skills.FirstOrDefault(s => s.Slug == slug);
    }

    public async Task<List<Skill>> GetEffectiveSkillsAsync(Guid agentId, string? groupId = null, CancellationToken ct = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{agentId}:{groupId}";
        if (cache.TryGetValue(cacheKey, out List<Skill>? cached) && cached is not null)
            return cached;

        // Mandatory skills (highest priority — always included, cannot be overridden)
        var mandatory = await db.Skills
            .Where(s => s.IsMandatory && s.Status == SkillStatus.Active)
            .ToListAsync(ct);

        // Personal skills
        var personal = await db.Skills
            .Where(s => !s.IsMandatory && s.Scope == SkillScope.Personal && s.OwnerAgentId == agentId && s.Status == SkillStatus.Active)
            .ToListAsync(ct);

        // Group skills
        var group = groupId is not null
            ? await db.Skills
                .Where(s => !s.IsMandatory && s.Scope == SkillScope.Group && s.GroupId == groupId && s.Status == SkillStatus.Active)
                .ToListAsync(ct)
            : [];

        // Global skills (non-mandatory)
        var global = await db.Skills
            .Where(s => !s.IsMandatory && s.Scope == SkillScope.Global && s.Status == SkillStatus.Active)
            .ToListAsync(ct);

        // Merge with precedence: Mandatory > Personal > Group > Global (no duplicates by slug)
        var seen = new HashSet<string>();
        var result = new List<Skill>();
        foreach (var s in mandatory.Concat(personal).Concat(group).Concat(global))
        {
            if (seen.Add(s.Slug)) result.Add(s);
        }

        cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    public async Task<Skill> RegisterAsync(Skill skill, CancellationToken ct = default)
    {
        db.Skills.Add(skill);
        await db.SaveChangesAsync(ct);
        await InvalidateCacheAsync();
        logger.LogInformation("Skill {Slug} registered ({Scope})", skill.Slug, skill.Scope);
        return skill;
    }

    public async Task<Skill> ProposePersonalSkillAsync(Guid agentId, SkillProposal proposal, CancellationToken ct = default)
    {
        var skill = new Skill
        {
            Slug = proposal.Slug,
            Name = proposal.Name,
            Description = proposal.Description,
            Type = proposal.Type,
            Scope = SkillScope.Personal,
            OwnerAgentId = agentId,
            Definition = proposal.Definition,
            Status = SkillStatus.Active  // personal skills auto-approved
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync(ct);
        await InvalidateCacheAsync();
        logger.LogInformation("Agent {AgentId} created personal skill {Slug}", agentId, skill.Slug);
        return skill;
    }

    public async Task<Skill> ProposeGlobalSkillAsync(Guid agentId, SkillProposal proposal, CancellationToken ct = default)
    {
        var skill = new Skill
        {
            Slug = proposal.Slug,
            Name = proposal.Name,
            Description = proposal.Description,
            Type = proposal.Type,
            Scope = proposal.Scope,
            GroupId = proposal.GroupId,
            Definition = proposal.Definition,
            Status = SkillStatus.PendingApproval
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync(ct);

        // Trigger HITL checkpoint for approval
        var context = new JsonObject
        {
            ["skillId"] = skill.Id.ToString(),
            ["slug"] = proposal.Slug,
            ["name"] = proposal.Name,
            ["type"] = proposal.Type.ToString(),
            ["scope"] = proposal.Scope.ToString(),
            ["justification"] = proposal.Justification,
            ["definition"] = proposal.Definition.ToJsonString(),
            ["proposedByAgentId"] = agentId.ToString()
        };

        await hitlQueue.EnqueueAsync(new HitlCheckpoint
        {
            FlowRunId = Guid.Empty,
            FlowNodeId = "skill-proposal",
            Type = CheckpointType.Approval,
            Title = $"Approve new skill: /{proposal.Slug}",
            Description = $"Agent {agentId} proposes a new {proposal.Scope} skill. Justification: {proposal.Justification}",
            Context = context
        }, ct);

        logger.LogInformation("Agent {AgentId} proposed {Scope} skill {Slug} — pending HITL approval", agentId, proposal.Scope, skill.Slug);
        return skill;
    }

    public async Task<Skill> ApproveSkillAsync(Guid skillId, string approvedBy, CancellationToken ct = default)
    {
        var skill = await db.Skills.FindAsync([skillId], ct)
            ?? throw new KeyNotFoundException($"Skill {skillId} not found");

        skill.Status = SkillStatus.Active;
        skill.ApprovedBy = approvedBy;
        skill.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await InvalidateCacheAsync();
        logger.LogInformation("Skill {Slug} approved by {By}", skill.Slug, approvedBy);
        return skill;
    }

    public async Task<Skill> RejectSkillAsync(Guid skillId, string rejectedBy, string reason, CancellationToken ct = default)
    {
        var skill = await db.Skills.FindAsync([skillId], ct)
            ?? throw new KeyNotFoundException($"Skill {skillId} not found");

        skill.Status = SkillStatus.Rejected;
        skill.ApprovedBy = $"Rejected by {rejectedBy}: {reason}";
        skill.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await InvalidateCacheAsync();
        logger.LogInformation("Skill {Slug} rejected by {By}: {Reason}", skill.Slug, rejectedBy, reason);
        return skill;
    }

    public async Task<List<SlashCommand>> GetSlashCommandsAsync(Guid agentId, CancellationToken ct = default)
    {
        var skills = await GetEffectiveSkillsAsync(agentId, null, ct);
        return skills
            .Where(s => s.Type == SkillType.SlashCommand)
            .Select(s => new SlashCommand
            {
                Slug = s.Slug,
                Name = s.Name,
                Description = s.Description,
                SkillId = s.Id
            })
            .ToList();
    }

    public Task InvalidateCacheAsync()
    {
        // Simple: clear entire cache. In production, use tag-based eviction.
        if (cache is MemoryCache mc) mc.Compact(1.0);
        return Task.CompletedTask;
    }
}
