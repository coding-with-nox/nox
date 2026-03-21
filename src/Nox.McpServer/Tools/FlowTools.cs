using ModelContextProtocol.Server;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.McpServer.Tools;

[McpServerToolType]
public static class FlowTools
{
    [McpServerTool]
    [Description("List all available flows for a project")]
    public static async Task<string> ListFlows(
        [Description("Project ID (UUID)")] string projectId,
        IFlowRepository flowRepo)
    {
        var flows = await flowRepo.ListByProjectAsync(Guid.Parse(projectId));
        return JsonSerializer.Serialize(flows.Select(f => new
        {
            id = f.Id,
            name = f.Name,
            description = f.Description,
            status = f.Status.ToString(),
            version = f.Version
        }));
    }

    [McpServerTool]
    [Description("Start a named flow with initial variables")]
    public static async Task<string> StartFlow(
        [Description("Flow name or ID")] string flowNameOrId,
        [Description("Initial variables as JSON object")] string variablesJson,
        IFlowEngine flowEngine,
        IFlowRepository flowRepo)
    {
        var isGuid = Guid.TryParse(flowNameOrId, out var flowId);
        if (!isGuid)
        {
            var flow = await flowRepo.FindByNameAsync(flowNameOrId);
            if (flow is null) return $"Flow '{flowNameOrId}' not found.";
            flowId = flow.Id;
        }

        var variables = JsonObject.Parse(variablesJson)?.AsObject() ?? new JsonObject();
        var run = await flowEngine.StartAsync(flowId, variables);
        return JsonSerializer.Serialize(new
        {
            flowRunId = run.Id,
            flowId = run.FlowId,
            status = run.Status.ToString(),
            startedAt = run.StartedAt
        });
    }

    [McpServerTool]
    [Description("Get the status and current state of a running flow")]
    public static async Task<string> GetFlowStatus(
        [Description("Flow Run ID (UUID)")] string flowRunId,
        IFlowEngine flowEngine)
    {
        var run = await flowEngine.GetRunAsync(Guid.Parse(flowRunId));
        if (run is null) return $"Flow run '{flowRunId}' not found.";

        return JsonSerializer.Serialize(new
        {
            flowRunId = run.Id,
            flowId = run.FlowId,
            status = run.Status.ToString(),
            currentNodeIds = run.CurrentNodeIds,
            startedAt = run.StartedAt,
            completedAt = run.CompletedAt,
            error = run.Error
        });
    }

    [McpServerTool]
    [Description("List all pending HITL checkpoints that require human review")]
    public static async Task<string> ListPendingCheckpoints(IHitlQueue hitlQueue)
    {
        var checkpoints = await hitlQueue.GetAllPendingAsync();
        return JsonSerializer.Serialize(checkpoints.Select(c => new
        {
            id = c.Id,
            flowRunId = c.FlowRunId,
            type = c.Type.ToString(),
            title = c.Title,
            description = c.Description,
            createdAt = c.CreatedAt,
            expiresAt = c.ExpiresAt
        }));
    }

    [McpServerTool]
    [Description("Submit a decision for a HITL checkpoint")]
    public static async Task<string> SubmitCheckpointDecision(
        [Description("Checkpoint ID (UUID)")] string checkpointId,
        [Description("Decision: Approved, Rejected, or Modified")] string decision,
        [Description("Your name or identifier")] string decidedBy,
        [Description("Optional payload as JSON")] string? payloadJson,
        IHitlQueue hitlQueue)
    {
        var id = Guid.Parse(checkpointId);
        var payload = payloadJson is not null
            ? JsonObject.Parse(payloadJson)?.AsObject()
            : null;

        await hitlQueue.SubmitDecisionAsync(id, new HitlDecision
        {
            CheckpointId = id,
            Decision = decision,
            Payload = payload,
            DecidedBy = decidedBy
        });

        return $"Decision '{decision}' submitted for checkpoint {checkpointId}.";
    }
}
