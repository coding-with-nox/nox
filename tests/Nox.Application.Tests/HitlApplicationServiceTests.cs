using Microsoft.Extensions.Logging.Abstractions;
using Nox.Application.Commands;
using Nox.Application.Services;
using Nox.Domain;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;

namespace Nox.Application.Tests;

public class HitlApplicationServiceTests
{
    private readonly IHitlQueue _queue = Substitute.For<IHitlQueue>();
    private readonly IFlowEngine _flowEngine = Substitute.For<IFlowEngine>();
    private readonly HitlApplicationService _sut;

    public HitlApplicationServiceTests()
    {
        _sut = new HitlApplicationService(_queue, _flowEngine, NullLogger<HitlApplicationService>.Instance);
    }

    // ── SubmitDecisionAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitDecision_WhenCheckpointPending_SubmitsAndResumesFlow()
    {
        var checkpointId = Guid.NewGuid();
        var flowRunId = Guid.NewGuid();
        var checkpoint = MakeCheckpoint(checkpointId, flowRunId);

        _queue.GetPendingAsync(checkpointId).Returns(checkpoint);

        var cmd = new SubmitHitlDecisionCommand(checkpointId, "Approved", null, "manager@test.com");
        var result = await _sut.SubmitDecisionAsync(cmd);

        await _queue.Received(1).SubmitDecisionAsync(checkpointId, Arg.Is<HitlDecision>(d =>
            d.CheckpointId == checkpointId &&
            d.Decision == "Approved" &&
            d.DecidedBy == "manager@test.com"));

        await _flowEngine.Received(1).ResumeAsync(flowRunId, Arg.Is<HitlDecision>(d =>
            d.Decision == "Approved"));

        Assert.Equal("Approved", result.Decision);
        Assert.Equal("manager@test.com", result.DecisionBy);
        Assert.NotNull(result.ResolvedAt);
    }

    [Fact]
    public async Task SubmitDecision_WhenCheckpointNotFound_ThrowsKeyNotFoundException()
    {
        _queue.GetPendingAsync(Arg.Any<Guid>()).Returns((HitlCheckpoint?)null);

        var cmd = new SubmitHitlDecisionCommand(Guid.NewGuid(), "Approved", null, "user");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.SubmitDecisionAsync(cmd));

        await _queue.DidNotReceive().SubmitDecisionAsync(Arg.Any<Guid>(), Arg.Any<HitlDecision>());
        await _flowEngine.DidNotReceive().ResumeAsync(Arg.Any<Guid>(), Arg.Any<HitlDecision>());
    }

    [Fact]
    public async Task SubmitDecision_WhenFlowRunIdEmpty_DoesNotCallFlowEngine()
    {
        var checkpointId = Guid.NewGuid();
        var checkpoint = MakeCheckpoint(checkpointId, Guid.Empty); // no associated flow run

        _queue.GetPendingAsync(checkpointId).Returns(checkpoint);

        var cmd = new SubmitHitlDecisionCommand(checkpointId, "Rejected", null, "user");
        await _sut.SubmitDecisionAsync(cmd);

        await _flowEngine.DidNotReceive().ResumeAsync(Arg.Any<Guid>(), Arg.Any<HitlDecision>());
    }

    [Fact]
    public async Task SubmitDecision_WhenFlowEngineThrows_StillReturnsResult()
    {
        // grain resume failure is non-fatal — decision is already persisted
        var checkpointId = Guid.NewGuid();
        var checkpoint = MakeCheckpoint(checkpointId, Guid.NewGuid());
        _queue.GetPendingAsync(checkpointId).Returns(checkpoint);
        _flowEngine.ResumeAsync(Arg.Any<Guid>(), Arg.Any<HitlDecision>())
            .Returns<Task>(_ => throw new InvalidOperationException("grain unavailable"));

        var cmd = new SubmitHitlDecisionCommand(checkpointId, "Approved", null, "user");
        var result = await _sut.SubmitDecisionAsync(cmd); // must NOT throw

        Assert.Equal("Approved", result.Decision);
    }

    // ── EscalateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Escalate_WhenCheckpointPending_CallsQueue()
    {
        var checkpointId = Guid.NewGuid();
        _queue.GetPendingAsync(checkpointId).Returns(MakeCheckpoint(checkpointId, Guid.NewGuid()));

        await _sut.EscalateAsync(new EscalateCheckpointCommand(checkpointId, "Out of time"));

        await _queue.Received(1).EscalateAsync(checkpointId, "Out of time");
    }

    [Fact]
    public async Task Escalate_WhenCheckpointNotFound_ThrowsKeyNotFoundException()
    {
        _queue.GetPendingAsync(Arg.Any<Guid>()).Returns((HitlCheckpoint?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.EscalateAsync(new EscalateCheckpointCommand(Guid.NewGuid(), "reason")));

        await _queue.DidNotReceive().EscalateAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static HitlCheckpoint MakeCheckpoint(Guid id, Guid flowRunId) => new()
    {
        Id = id,
        FlowRunId = flowRunId,
        FlowNodeId = "node-test",
        Type = CheckpointType.Approval,
        Title = "Test checkpoint",
        Status = CheckpointStatus.Pending
    };
}
