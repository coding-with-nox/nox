using System.Text.Json.Nodes;

namespace Nox.Application.Commands;

public record StartFlowRunCommand(Guid FlowId, JsonObject? Variables);

public record CancelFlowRunCommand(Guid FlowRunId, string Reason);
