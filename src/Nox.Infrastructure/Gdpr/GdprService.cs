using Microsoft.EntityFrameworkCore;
using Nox.Domain.Gdpr;
using Nox.Infrastructure.Persistence;

namespace Nox.Infrastructure.Gdpr;

public sealed class GdprService(NoxDbContext db) : IGdprService
{
    private const string Anonymized = "[anonymized]";

    public async Task<GdprAnonymizeResult> AnonymizeUserAsync(string username, CancellationToken ct = default)
    {
        // HitlCheckpoints.DecisionBy
        var hitlCount = await db.HitlCheckpoints
            .Where(h => h.DecisionBy == username)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.DecisionBy, Anonymized), ct);

        // Skills.ApprovedBy — covers both approved ("username") and rejected ("Rejected by username: ...")
        var skillsCount = await db.Skills
            .Where(s => s.ApprovedBy == username || (s.ApprovedBy != null && s.ApprovedBy.Contains(username)))
            .ExecuteUpdateAsync(s => s.SetProperty(sk => sk.ApprovedBy, Anonymized), ct);

        // McpServers.ApprovedBy
        var mcpCount = await db.McpServers
            .Where(m => m.ApprovedBy == username)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ApprovedBy, Anonymized), ct);

        // Flows.CreatedBy
        var flowCount = await db.Flows
            .Where(f => f.CreatedBy == username)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreatedBy, Anonymized), ct);

        // AiAuditLog.DecidedBy — preserve the record but anonymize who decided
        await db.AiAuditLog
            .Where(a => a.DecidedBy == username)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DecidedBy, Anonymized), ct);

        return new GdprAnonymizeResult(
            OriginalUsername:        username,
            ExecutedAt:              DateTimeOffset.UtcNow,
            HitlCheckpointsAffected: hitlCount,
            SkillsAffected:          skillsCount,
            McpServersAffected:      mcpCount,
            FlowsAffected:           flowCount);
    }
}
