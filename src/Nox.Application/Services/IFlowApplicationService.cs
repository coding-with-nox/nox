using Nox.Application.Commands;
using Nox.Domain.Flows;

namespace Nox.Application.Services;

public interface IFlowApplicationService
{
    Task<FlowRun> StartRunAsync(StartFlowRunCommand command, CancellationToken ct = default);
    Task CancelRunAsync(CancelFlowRunCommand command, CancellationToken ct = default);
}
