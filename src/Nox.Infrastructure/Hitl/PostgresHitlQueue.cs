using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Hitl;
using Nox.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Nox.Infrastructure.Hitl;

public class PostgresHitlQueue(
    NoxDbContext db,
    IConnectionMultiplexer redis,
    ILogger<PostgresHitlQueue> logger) : IHitlQueue
{
    private const string PendingChannel = "nox:hitl:pending";
    private const string DecisionPrefix = "nox:hitl:decision:";

    public async Task<Guid> EnqueueAsync(HitlCheckpoint checkpoint, CancellationToken ct = default)
    {
        db.HitlCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync(ct);

        // Publish notification for real-time dashboard
        var pub = redis.GetSubscriber();
        await pub.PublishAsync(RedisChannel.Literal(PendingChannel), checkpoint.Id.ToString());

        logger.LogInformation("HITL checkpoint {Id} enqueued: {Title}", checkpoint.Id, checkpoint.Title);
        return checkpoint.Id;
    }

    public async Task<HitlDecision> WaitForDecisionAsync(Guid checkpointId, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<HitlDecision>();
        var channel = RedisChannel.Literal($"{DecisionPrefix}{checkpointId}");

        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(channel, (_, message) =>
        {
            try
            {
                var json = (string?)message;
                if (json is not null)
                {
                    var decision = System.Text.Json.JsonSerializer.Deserialize<HitlDecision>(json);
                    if (decision is not null) tcs.TrySetResult(decision);
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        ct.Register(() => tcs.TrySetCanceled());

        return await tcs.Task;
    }

    public async Task<HitlCheckpoint?> GetPendingAsync(Guid checkpointId)
    {
        return await db.HitlCheckpoints
            .FirstOrDefaultAsync(h => h.Id == checkpointId && h.Status == CheckpointStatus.Pending);
    }

    public async Task<List<HitlCheckpoint>> GetPendingByFlowAsync(Guid flowRunId)
    {
        return await db.HitlCheckpoints
            .Where(h => h.FlowRunId == flowRunId && h.Status == CheckpointStatus.Pending)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<HitlCheckpoint>> GetAllPendingAsync(int skip = 0, int take = 50)
    {
        return await db.HitlCheckpoints
            .Where(h => h.Status == CheckpointStatus.Pending)
            .OrderBy(h => h.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(h => h.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task SubmitDecisionAsync(Guid checkpointId, HitlDecision decision)
    {
        // Atomic check: only update if still Pending — prevents double-decision race condition
        var checkpoint = await db.HitlCheckpoints
            .FirstOrDefaultAsync(h => h.Id == checkpointId && h.Status == CheckpointStatus.Pending);

        if (checkpoint is null)
        {
            var exists = await db.HitlCheckpoints.AnyAsync(h => h.Id == checkpointId);
            throw exists
                ? new InvalidOperationException($"Checkpoint {checkpointId} has already been resolved")
                : new KeyNotFoundException($"Checkpoint {checkpointId} not found");
        }

        checkpoint.Status = decision.Decision switch
        {
            "Approved" => CheckpointStatus.Approved,
            "Rejected" => CheckpointStatus.Rejected,
            "Modified" => CheckpointStatus.Modified,
            _ => CheckpointStatus.Approved
        };
        checkpoint.Decision = decision.Decision;
        checkpoint.DecisionPayload = decision.Payload;
        checkpoint.DecisionBy = decision.DecidedBy;
        checkpoint.ResolvedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        // Notify waiting grain via Redis pub/sub
        var json = System.Text.Json.JsonSerializer.Serialize(decision);
        var pub = redis.GetSubscriber();
        await pub.PublishAsync(RedisChannel.Literal($"{DecisionPrefix}{checkpointId}"), json);

        logger.LogInformation("HITL decision submitted for {Id}: {Decision} by {By}",
            checkpointId, decision.Decision, decision.DecidedBy);
    }

    public async Task EscalateAsync(Guid checkpointId, string reason)
    {
        var checkpoint = await db.HitlCheckpoints.FindAsync(checkpointId)
            ?? throw new KeyNotFoundException($"Checkpoint {checkpointId} not found");

        checkpoint.Status = CheckpointStatus.Escalated;
        checkpoint.DecisionBy = $"Escalated: {reason}";
        await db.SaveChangesAsync();

        logger.LogWarning("HITL checkpoint {Id} escalated: {Reason}", checkpointId, reason);
    }
}
