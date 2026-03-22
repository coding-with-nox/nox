namespace Nox.Domain.Gdpr;

/// <summary>
/// GDPR Right to Erasure (Art. 17) — anonymizes all personal identifiers
/// for a given username across the Nox database.
/// Strategy: replace the username with "[anonymized]" in all tables.
/// Records are preserved for operational/audit integrity.
/// </summary>
public interface IGdprService
{
    /// <summary>
    /// Anonymize all occurrences of <paramref name="username"/> in the database.
    /// Returns a summary of how many records were affected per entity type.
    /// </summary>
    Task<GdprAnonymizeResult> AnonymizeUserAsync(string username, CancellationToken ct = default);
}

public record GdprAnonymizeResult(
    string OriginalUsername,
    DateTimeOffset ExecutedAt,
    int HitlCheckpointsAffected,
    int SkillsAffected,
    int McpServersAffected,
    int FlowsAffected);
