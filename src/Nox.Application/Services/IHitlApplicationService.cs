using Nox.Application.Commands;
using Nox.Domain.Hitl;

namespace Nox.Application.Services;

public interface IHitlApplicationService
{
    /// <summary>
    /// Submit a decision for a pending HITL checkpoint and resume the associated flow run.
    /// Returns the resolved checkpoint. Throws KeyNotFoundException if not found, InvalidOperationException if already resolved.
    /// </summary>
    Task<HitlCheckpoint> SubmitDecisionAsync(SubmitHitlDecisionCommand command, CancellationToken ct = default);

    Task EscalateAsync(EscalateCheckpointCommand command, CancellationToken ct = default);
}
