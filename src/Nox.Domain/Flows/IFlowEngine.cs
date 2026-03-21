using System.Text.Json.Nodes;

namespace Nox.Domain.Flows;

public interface IFlowEngine
{
    Task<FlowRun> StartAsync(Guid flowId, JsonObject? variables = null, CancellationToken ct = default);
    Task<FlowRun?> GetRunAsync(Guid flowRunId);
    Task PauseAsync(Guid flowRunId, string reason);
    Task ResumeAsync(Guid flowRunId, Hitl.HitlDecision decision);
    Task CancelAsync(Guid flowRunId, string reason);
    IAsyncEnumerable<FlowEvent> SubscribeToEventsAsync(Guid flowRunId, CancellationToken ct);
}
