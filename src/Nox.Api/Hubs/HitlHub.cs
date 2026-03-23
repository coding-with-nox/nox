using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Nox.Api.Auth;
using Nox.Domain.Hitl;

namespace Nox.Api.Hubs;

/// <summary>
/// SignalR hub for real-time HITL dashboard updates.
/// Clients subscribe to checkpoint notifications.
/// </summary>
[Authorize(Policy = NoxPolicies.AnyUser)]
public class HitlHub : Hub
{
    public async Task SubscribeToFlow(string flowRunId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"flow-{flowRunId}");
    }

    public async Task UnsubscribeFromFlow(string flowRunId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"flow-{flowRunId}");
    }
}

/// <summary>
/// SignalR hub for real-time agent monitoring.
/// </summary>
[Authorize(Policy = NoxPolicies.AnyUser)]
public class AgentMonitorHub : Hub
{
    public async Task SubscribeToRun(string flowRunId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"run-{flowRunId}");
    }
}
