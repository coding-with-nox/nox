namespace Nox.Domain.Audit;

/// <summary>
/// Immutable audit record for every AI operation.
/// Required by EU AI Act Art. 12: logs must be kept for at least 6 months.
/// No PII stored — only hashes and summaries.
/// </summary>
public class AiAuditEntry
{
    public Guid   Id          { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // Context
    public required Guid   AgentId    { get; init; }
    public required Guid   FlowRunId  { get; init; }
    public required string EventType  { get; init; } // e.g. task.started, task.completed, hitl.decided

    // AI model info
    public required string ModelUsed   { get; init; }
    public int InputTokens  { get; init; }
    public int OutputTokens { get; init; }

    // HITL decisions (nullable — only set when EventType == hitl.*)
    public string? DecidedBy { get; init; }  // username from JWT
    public string? Decision  { get; init; }

    // Input/output are NEVER stored in plain text — only SHA-256 hashes for auditability
    public string? InputHash  { get; init; }
    public string? OutputHash { get; init; }

    // Short human-readable summary (no PII, max 500 chars)
    public string? Summary { get; init; }

    // Retention — minimum 6 months per EU AI Act Art. 12
    public DateTimeOffset RetainUntil { get; init; } = DateTimeOffset.UtcNow.AddMonths(6);
}
