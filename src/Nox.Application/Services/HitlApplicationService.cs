using Microsoft.Extensions.Logging;
using Nox.Application.Commands;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;

namespace Nox.Application.Services;

public class HitlApplicationService(
    IHitlQueue hitlQueue,
    IFlowEngine flowEngine,
    ILogger<HitlApplicationService> logger) : IHitlApplicationService
{
    public async Task<HitlCheckpoint> SubmitDecisionAsync(SubmitHitlDecisionCommand command, CancellationToken ct = default)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(command.CheckpointId)
            ?? throw new KeyNotFoundException($"Checkpoint {command.CheckpointId} not found or already resolved");

        var decision = new HitlDecision
        {
            CheckpointId = command.CheckpointId,
            Decision     = command.Decision,
            Payload      = command.Payload,
            DecidedBy    = command.DecidedBy
        };

        await hitlQueue.SubmitDecisionAsync(command.CheckpointId, decision);

        if (checkpoint.FlowRunId != Guid.Empty)
        {
            try
            {
                await flowEngine.ResumeAsync(checkpoint.FlowRunId, decision);
            }
            catch (Exception ex)
            {
                // Non-fatal: decision is persisted, grain resume failure is logged and retried by Orleans reminder
                logger.LogWarning(ex, "Could not resume flow grain {FlowRunId} after HITL decision", checkpoint.FlowRunId);
            }
        }

        checkpoint.Decision   = command.Decision;
        checkpoint.DecisionBy = command.DecidedBy;
        checkpoint.ResolvedAt = DateTimeOffset.UtcNow;
        return checkpoint;
    }

    public async Task EscalateAsync(EscalateCheckpointCommand command, CancellationToken ct = default)
    {
        var checkpoint = await hitlQueue.GetPendingAsync(command.CheckpointId)
            ?? throw new KeyNotFoundException($"Checkpoint {command.CheckpointId} not found or already resolved");

        await hitlQueue.EscalateAsync(command.CheckpointId, command.Reason);
        logger.LogInformation("Checkpoint {Id} escalated: {Reason}", command.CheckpointId, command.Reason);
    }
}
