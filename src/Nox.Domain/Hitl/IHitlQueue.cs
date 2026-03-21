namespace Nox.Domain.Hitl;

public interface IHitlQueue
{
    Task<Guid> EnqueueAsync(HitlCheckpoint checkpoint, CancellationToken ct = default);
    Task<HitlDecision> WaitForDecisionAsync(Guid checkpointId, CancellationToken ct = default);
    Task<HitlCheckpoint?> GetPendingAsync(Guid checkpointId);
    Task<List<HitlCheckpoint>> GetPendingByFlowAsync(Guid flowRunId);
    Task<List<HitlCheckpoint>> GetAllPendingAsync(int skip = 0, int take = 50);
    Task SubmitDecisionAsync(Guid checkpointId, HitlDecision decision);
    Task EscalateAsync(Guid checkpointId, string reason);
}
