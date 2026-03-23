using Nox.Application.Commands;
using Nox.Application.Services;
using Nox.Domain.Flows;
using System.Text.Json.Nodes;

namespace Nox.Application.Tests;

public class FlowApplicationServiceTests
{
    private readonly IFlowEngine _flowEngine = Substitute.For<IFlowEngine>();
    private readonly FlowApplicationService _sut;

    public FlowApplicationServiceTests()
    {
        _sut = new FlowApplicationService(_flowEngine);
    }

    [Fact]
    public async Task StartRun_DelegatesTo_FlowEngine()
    {
        var flowId = Guid.NewGuid();
        var variables = new JsonObject { ["env"] = "test" };
        var expected = new FlowRun { Id = Guid.NewGuid(), FlowId = flowId };

        _flowEngine.StartAsync(flowId, variables, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.StartRunAsync(new StartFlowRunCommand(flowId, variables));

        Assert.Equal(expected.Id, result.Id);
        await _flowEngine.Received(1).StartAsync(flowId, variables, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelRun_DelegatesTo_FlowEngine()
    {
        var runId = Guid.NewGuid();

        await _sut.CancelRunAsync(new CancelFlowRunCommand(runId, "user request"));

        await _flowEngine.Received(1).CancelAsync(runId, "user request");
    }
}
