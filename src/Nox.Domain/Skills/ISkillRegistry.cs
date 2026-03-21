namespace Nox.Domain.Skills;

public interface ISkillRegistry
{
    Task<Skill?> ResolveAsync(string slug, Guid agentId, string? groupId = null, CancellationToken ct = default);
    Task<List<Skill>> GetEffectiveSkillsAsync(Guid agentId, string? groupId = null, CancellationToken ct = default);
    Task<Skill> RegisterAsync(Skill skill, CancellationToken ct = default);
    Task<Skill> ProposePersonalSkillAsync(Guid agentId, SkillProposal proposal, CancellationToken ct = default);
    Task<Skill> ProposeGlobalSkillAsync(Guid agentId, SkillProposal proposal, CancellationToken ct = default);
    Task<Skill> ApproveSkillAsync(Guid skillId, string approvedBy, CancellationToken ct = default);
    Task<Skill> RejectSkillAsync(Guid skillId, string rejectedBy, string reason, CancellationToken ct = default);
    Task<List<SlashCommand>> GetSlashCommandsAsync(Guid agentId, CancellationToken ct = default);
    Task InvalidateCacheAsync();
}
