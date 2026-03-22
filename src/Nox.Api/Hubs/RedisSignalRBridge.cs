using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using System.Text.Json;

namespace Nox.Api.Hubs;

/// <summary>
/// Background service that bridges Redis pub/sub to SignalR.
/// Forwards HITL and agent events from grains to connected dashboard clients.
/// </summary>
public class RedisSignalRBridge(
    IConnectionMultiplexer redis,
    IHubContext<HitlHub> hitlHub,
    IHubContext<AgentMonitorHub> agentHub,
    ILogger<RedisSignalRBridge> logger) : BackgroundService
{
    private const string HitlPendingChannel = "nox:hitl:pending";
    private const string FlowEventsPattern = "nox:flow:*:events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();

        // HITL checkpoint created → notify dashboard
        await sub.SubscribeAsync(
            RedisChannel.Literal(HitlPendingChannel),
            async (_, checkpointId) =>
            {
                try
                {
                    await hitlHub.Clients.All.SendAsync(
                        "CheckpointCreated",
                        new { checkpointId = (string?)checkpointId },
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to forward CheckpointCreated to SignalR");
                }
            });

        // Flow events → forward to agent monitor hub
        await sub.SubscribeAsync(
            RedisChannel.Pattern(FlowEventsPattern),
            async (channel, message) =>
            {
                try
                {
                    var json = (string?)message;
                    if (json is null) return;

                    var payload = JsonDocument.Parse(json).RootElement;
                    var flowRunId = payload.TryGetProperty("flowRunId", out var fid)
                        ? fid.GetString() : null;
                    var eventType = payload.TryGetProperty("eventType", out var et)
                        ? et.GetString() : null;

                    // Notify group for specific flow run
                    if (flowRunId is not null)
                    {
                        await agentHub.Clients
                            .Group($"run-{flowRunId}")
                            .SendAsync("FlowEvent", new
                            {
                                flowRunId,
                                eventType,
                                payload = json
                            }, stoppingToken);
                    }

                    // Also broadcast agent updates for monitor page
                    if (eventType?.StartsWith("agent.") == true)
                    {
                        await agentHub.Clients.All.SendAsync(
                            "AgentUpdated",
                            new { flowRunId, eventType },
                            stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to forward flow event to SignalR");
                }
            });

        logger.LogInformation("Redis→SignalR bridge started");

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await sub.UnsubscribeAllAsync();
        logger.LogInformation("Redis→SignalR bridge stopped");
    }
}
