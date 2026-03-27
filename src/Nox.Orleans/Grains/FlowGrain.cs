using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Agents;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using Nox.Orleans.GrainInterfaces;
using Nox.Orleans.States;
using Orleans;
using Orleans.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;
using StackExchange.Redis;

namespace Nox.Orleans.Grains;

public class FlowGrain(
    [PersistentState("flow", "NoxStore")] IPersistentState<FlowState> state,
    IHitlQueue hitlQueue,
    ILogger<FlowGrain> logger)
    : Grain, IFlowGrain, IRemindable
{
    private const string CheckpointReminderPrefix = "checkpoint-";

    public async Task<FlowRunState> StartAsync(FlowStartRequest request)
    {
        var flowRunId = this.GetGrainId().GetGuidKey();

        state.State.FlowRunId = flowRunId;
        state.State.FlowId = request.FlowId;
        state.State.ProjectId = request.ProjectId;
        state.State.Status = FlowRunStatus.Running;
        state.State.Variables = request.Variables;
        state.State.StartedAt = DateTimeOffset.UtcNow;

        // Load flow graph
        var flowResolver = ServiceProvider.GetRequiredService<IFlowResolver>();
        var flow = await flowResolver.ResolveAsync(request.FlowId);
        state.State.FlowGraphJson = JsonSerializer.Serialize(flow.Graph);

        // Find start node
        var startNode = flow.Graph.GetStartNode();
        state.State.CurrentNodeIds = [startNode.Id];

        await state.WriteStateAsync();
        logger.LogInformation("Flow run {FlowRunId} started for flow {FlowId}", flowRunId, request.FlowId);

        // Publish start event
        await PublishEventAsync("flow.started", startNode.Id);

        // Advance from start node immediately
        await AdvanceAsync(startNode.Id, new TaskResult { TaskId = Guid.NewGuid(), Success = true });

        return await GetStateAsync();
    }

    public async Task AdvanceAsync(string completedNodeId, TaskResult result)
    {
        if (state.State.Status is FlowRunStatus.Cancelled or FlowRunStatus.Failed or FlowRunStatus.Completed)
            return;

        var graph = JsonSerializer.Deserialize<FlowGraph>(state.State.FlowGraphJson)!;

        // Store node result
        state.State.NodeResults[completedNodeId] = result.Output.ToJsonString();

        // Inject agent output summary into flow variables so downstream agents have context
        if (result.Success && result.Output.TryGetPropertyValue("result", out var agentOutput) && agentOutput is not null)
        {
            var vars = JsonObject.Parse(state.State.Variables)?.AsObject() ?? new JsonObject();
            var key = "output_" + completedNodeId.Replace("-", "_");
            vars[key] = agentOutput.DeepClone();
            state.State.Variables = vars.ToJsonString();
        }

        // Remove from current nodes
        state.State.CurrentNodeIds.Remove(completedNodeId);

        await PublishEventAsync("node.completed", completedNodeId, new JsonObject
        {
            ["success"] = result.Success,
            ["tokensUsed"] = result.TokensUsed
        });

        if (!result.Success)
        {
            state.State.Status = FlowRunStatus.Failed;
            state.State.Error = result.Error;
            await state.WriteStateAsync();
            await PublishEventAsync("flow.failed", completedNodeId);
            return;
        }

        // Find outgoing edges
        var outgoing = graph.GetOutgoingEdges(completedNodeId).ToList();
        var variables = JsonObject.Parse(state.State.Variables)?.AsObject() ?? new JsonObject();

        foreach (var edge in outgoing)
        {
            // Evaluate condition
            if (edge.Condition is not null && !EvaluateCondition(edge.Condition, variables, result))
                continue;

            var nextNode = graph.GetNode(edge.ToNodeId);
            if (nextNode is null) continue;

            await ProcessNodeAsync(nextNode, graph, variables);
        }

        await state.WriteStateAsync();

        // Check if flow completed
        if (!state.State.CurrentNodeIds.Any() && state.State.PendingCheckpointIds.Count == 0)
        {
            state.State.Status = FlowRunStatus.Completed;
            state.State.CompletedAt = DateTimeOffset.UtcNow;
            await state.WriteStateAsync();
            await PublishEventAsync("flow.completed", completedNodeId);
            logger.LogInformation("Flow run {FlowRunId} completed", state.State.FlowRunId);
        }
    }

    private async Task ProcessNodeAsync(FlowNode node, FlowGraph graph, JsonObject variables)
    {
        state.State.CurrentNodeIds.Add(node.Id);

        switch (node.NodeType)
        {
            case NodeType.End:
                state.State.CurrentNodeIds.Remove(node.Id);
                state.State.Status = FlowRunStatus.Completed;
                state.State.CompletedAt = DateTimeOffset.UtcNow;
                await PublishEventAsync("flow.completed", node.Id);
                break;

            case NodeType.AgentTask:
                await SpawnAgentTaskAsync(node, variables);
                break;

            case NodeType.HitlCheckpoint:
                await CreateHitlCheckpointAsync(node, variables);
                break;

            case NodeType.Decision:
                state.State.CurrentNodeIds.Remove(node.Id);
                var decisionEdges = graph.GetOutgoingEdges(node.Id).ToList();
                foreach (var edge in decisionEdges)
                {
                    if (edge.Condition is null || EvaluateCondition(edge.Condition, variables, null))
                    {
                        var next = graph.GetNode(edge.ToNodeId);
                        if (next is not null) await ProcessNodeAsync(next, graph, variables);
                        break; // Decision picks one branch
                    }
                }
                break;

            case NodeType.Fork:
                // Fork fans out — all branches run in parallel via the current grain
                state.State.CurrentNodeIds.Remove(node.Id);
                var branches = node.Config.TryGetPropertyValue("branches", out var b)
                    ? b?.AsArray().Select(x => x?.GetValue<string>() ?? "").ToList() ?? []
                    : graph.GetOutgoingEdges(node.Id).Select(e => e.ToNodeId).ToList();

                state.State.ForkJoinCounter = branches.Count;
                foreach (var branchId in branches)
                {
                    var branchNode = graph.GetNode(branchId);
                    if (branchNode is not null) await ProcessNodeAsync(branchNode, graph, variables);
                }
                break;

            case NodeType.Join:
                state.State.ForkJoinCounter--;
                state.State.CurrentNodeIds.Remove(node.Id);
                if (state.State.ForkJoinCounter <= 0)
                {
                    // All branches done — advance past join
                    var joinEdges = graph.GetOutgoingEdges(node.Id).ToList();
                    foreach (var e in joinEdges)
                    {
                        var next = graph.GetNode(e.ToNodeId);
                        if (next is not null) await ProcessNodeAsync(next, graph, variables);
                    }
                }
                break;
        }
    }

    private async Task SpawnAgentTaskAsync(FlowNode node, JsonObject variables)
    {
        if (node.AgentTemplateId is null) return;

        var agentId = Guid.NewGuid();
        var agent = GrainFactory.GetGrain<IAgentGrain>(agentId);

        await agent.InitializeAsync(new AgentInitRequest
        {
            TemplateId = node.AgentTemplateId.Value,
            FlowRunId = state.State.FlowRunId,
            ProjectId = state.State.ProjectId,
            MaxSubAgents = node.Config.TryGetPropertyValue("maxSubAgents", out var ms)
                ? ms?.GetValue<int>() ?? 3 : 3
        });

        state.State.SpawnedAgentIds.Add(agentId);

        // GrainCancellationTokenSource must be created inside the Orleans execution context (before Task.Run)
        var cts = new GrainCancellationTokenSource();
        var capturedNodeId  = node.Id;
        var capturedRunId   = state.State.FlowRunId;
        var capturedPayload = variables;
        var flowRef = this.AsReference<IFlowGrain>();

        // Execute task asynchronously (fire and forget — grain will call AdvanceAsync when done)
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await agent.ExecuteTaskAsync(new TaskInput
                {
                    FlowNodeId = capturedNodeId,
                    FlowRunId  = capturedRunId,
                    Payload    = capturedPayload
                }, cts.Token);

                await flowRef.AdvanceAsync(capturedNodeId, result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in background agent task for node {NodeId}", capturedNodeId);
                await flowRef.AdvanceAsync(capturedNodeId, new TaskResult
                {
                    TaskId  = Guid.NewGuid(),
                    Success = false,
                    Error   = ex.Message
                });
            }
        });

        await PublishEventAsync("agent.spawned", node.Id, new JsonObject { ["agentId"] = agentId.ToString() });
    }

    private async Task CreateHitlCheckpointAsync(FlowNode node, JsonObject variables)
    {
        var config = node.Config;
        var checkpointType = config.TryGetPropertyValue("checkpointType", out var ct)
            ? Enum.Parse<CheckpointType>(ct?.GetValue<string>() ?? "Approval")
            : CheckpointType.Approval;

        var checkpoint = new HitlCheckpoint
        {
            FlowRunId = state.State.FlowRunId,
            FlowNodeId = node.Id,
            Type = checkpointType,
            Title = config.TryGetPropertyValue("title", out var t) ? t?.GetValue<string>() ?? node.Label : node.Label,
            Description = config.TryGetPropertyValue("description", out var d) ? d?.GetValue<string>() : null,
            Context = variables,
            ExpiresAt = config.TryGetPropertyValue("expiresInHours", out var exp)
                ? DateTimeOffset.UtcNow.AddHours(exp?.GetValue<double>() ?? 24)
                : null
        };

        var checkpointId = await hitlQueue.EnqueueAsync(checkpoint);
        state.State.PendingCheckpointIds.Add(checkpointId);
        state.State.CurrentNodeIds.Remove(node.Id);

        // Set Orleans reminder to avoid losing checkpoint on silo restart
        await this.RegisterOrUpdateReminder(
            $"{CheckpointReminderPrefix}{checkpointId}",
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromHours(1));

        logger.LogInformation("HITL checkpoint {CheckpointId} created for node {NodeId}", checkpointId, node.Id);
        await PublishEventAsync("hitl.checkpoint.created", node.Id, new JsonObject
        {
            ["checkpointId"] = checkpointId.ToString(),
            ["type"] = checkpointType.ToString()
        });
    }

    public async Task PauseAtCheckpointAsync(Guid checkpointId)
    {
        state.State.Status = FlowRunStatus.Paused;
        await state.WriteStateAsync();
    }

    public async Task ResumeFromCheckpointAsync(Guid checkpointId, HitlDecision decision)
    {
        state.State.PendingCheckpointIds.Remove(checkpointId);

        // Cancel reminder
        try
        {
            var reminder = await this.GetReminder($"{CheckpointReminderPrefix}{checkpointId}");
            if (reminder is not null) await this.UnregisterReminder(reminder);
        }
        catch { /* reminder may not exist */ }

        // Find checkpoint node in graph
        var graph = JsonSerializer.Deserialize<FlowGraph>(state.State.FlowGraphJson)!;
        var checkpoint = await hitlQueue.GetPendingAsync(checkpointId);
        if (checkpoint is null) return;

        // Inject decision into variables
        var variables = JsonObject.Parse(state.State.Variables)?.AsObject() ?? new JsonObject();
        variables["decision"] = decision.Decision;
        if (decision.Payload is not null)
        {
            foreach (var kv in decision.Payload)
                variables[kv.Key] = kv.Value?.DeepClone();
        }
        state.State.Variables = variables.ToJsonString();

        state.State.Status = FlowRunStatus.Running;
        await state.WriteStateAsync();

        // Advance from the checkpoint node
        await AdvanceAsync(checkpoint.FlowNodeId, new TaskResult
        {
            TaskId = Guid.NewGuid(),
            Success = true,
            Output = new JsonObject { ["decision"] = decision.Decision }
        });

        logger.LogInformation("Flow run {FlowRunId} resumed from checkpoint {CheckpointId} with decision {Decision}",
            state.State.FlowRunId, checkpointId, decision.Decision);
    }

    public Task<FlowRunState> GetStateAsync() => Task.FromResult(new FlowRunState
    {
        FlowRunId = state.State.FlowRunId,
        FlowId = state.State.FlowId,
        Status = state.State.Status,
        CurrentNodeIds = state.State.CurrentNodeIds,
        PendingCheckpointIds = state.State.PendingCheckpointIds,
        Variables = state.State.Variables,
        StartedAt = state.State.StartedAt,
        CompletedAt = state.State.CompletedAt,
        Error = state.State.Error
    });

    public Task<List<string>> GetReadyNodeIdsAsync() =>
        Task.FromResult(state.State.CurrentNodeIds.ToList());

    public async Task CancelAsync(string reason)
    {
        state.State.Status = FlowRunStatus.Cancelled;
        state.State.Error = reason;
        await state.WriteStateAsync();
        await PublishEventAsync("flow.cancelled", "system");
        logger.LogInformation("Flow run {FlowRunId} cancelled: {Reason}", state.State.FlowRunId, reason);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName.StartsWith(CheckpointReminderPrefix))
        {
            var id = Guid.Parse(reminderName[CheckpointReminderPrefix.Length..]);
            var checkpoint = await hitlQueue.GetPendingAsync(id);
            if (checkpoint is not null && checkpoint.IsExpired)
            {
                logger.LogWarning("HITL checkpoint {Id} expired, auto-rejecting", id);
                await hitlQueue.EscalateAsync(id, "Checkpoint expired");
            }
        }
    }

    private bool EvaluateCondition(string condition, JsonObject variables, TaskResult? result)
    {
        // Simple condition evaluator: support "decision == 'Approved'" style
        try
        {
            if (condition.Contains("decision") && variables.TryGetPropertyValue("decision", out var dec))
            {
                var decValue = dec?.GetValue<string>() ?? "";
                if (condition.Contains("=="))
                {
                    var parts = condition.Split("==", 2);
                    var expected = parts[1].Trim().Trim('\'', '"');
                    return decValue.Equals(expected, StringComparison.OrdinalIgnoreCase);
                }
            }
            return true; // Default: edge is traversable
        }
        catch
        {
            return false;
        }
    }

    private async Task PublishEventAsync(string eventType, string nodeId, JsonObject? extra = null)
    {
        try
        {
            var redis = ServiceProvider.GetService<IConnectionMultiplexer>();
            if (redis is null) return;

            var payload = new JsonObject
            {
                ["flowRunId"] = state.State.FlowRunId.ToString(),
                ["eventType"] = eventType,
                ["nodeId"] = nodeId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            };

            if (extra is not null)
            {
                foreach (var kv in extra)
                    payload[kv.Key] = kv.Value?.DeepClone();
            }

            var pub = redis.GetSubscriber();
            await pub.PublishAsync(
                RedisChannel.Literal($"nox:flow:{state.State.FlowRunId}:events"),
                payload.ToJsonString());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish flow event {EventType}", eventType);
        }
    }
}

// Interface for flow resolution in grain context
public interface IFlowResolver
{
    Task<Flow> ResolveAsync(Guid flowId);
}
