using Nox.Application.Commands;
using Nox.Domain.Flows;

namespace Nox.Application.Services;

public class FlowApplicationService(IFlowEngine flowEngine) : IFlowApplicationService
{
    public Task<FlowRun> StartRunAsync(StartFlowRunCommand command, CancellationToken ct = default)
        => flowEngine.StartAsync(command.FlowId, command.Variables, ct);

    public Task CancelRunAsync(CancelFlowRunCommand command, CancellationToken ct = default)
        => flowEngine.CancelAsync(command.FlowRunId, command.Reason);
}
