using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nox.Domain;
using Nox.Domain.Agents;
using Nox.Domain.Hitl;
using Nox.Domain.Llm;
using Nox.Domain.Memory;
using Nox.Domain.Messages;
using Nox.Domain.Skills;
using Nox.Orleans.GrainInterfaces;
using Nox.Orleans.States;
using Orleans;
using Orleans.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Orleans.Grains;

public class AgentGrain(
    [PersistentState("agent", "NoxStore")] IPersistentState<AgentState> state,
    ILlmProvider llmProvider,
    IMemoryStore memoryStore,
    ISkillRegistry skillRegistry,
    IHitlQueue hitlQueue,
    ILogger<AgentGrain> logger)
    : Grain, IAgentGrain
{
    private readonly List<ChatMessage> _workingMemory = [];
    private TaskCompletionSource<HitlDecision>? _hitlTcs;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("AgentGrain {Id} activated", this.GetGrainId());
        return Task.CompletedTask;
    }

    public async Task InitializeAsync(AgentInitRequest request)
    {
        if (state.State.IsInitialized) return;

        // Load template from infrastructure (via service locator pattern for grain)
        var templateService = ServiceProvider.GetRequiredService<IAgentTemplateResolver>();
        var template = await templateService.ResolveAsync(request.TemplateId);

        state.State.Id = this.GetGrainId().GetGuidKey();
        state.State.TemplateId = request.TemplateId;
        state.State.FlowRunId = request.FlowRunId;
        state.State.ProjectId = request.ProjectId;
        state.State.ParentAgentId = request.ParentAgentId;
        state.State.Name = template.Name;
        state.State.Role = template.Role;
        state.State.SystemPrompt = template.SystemPromptTemplate;
        state.State.Model = template.DefaultModel;
        state.State.MaxSubAgents = request.MaxSubAgents;
        state.State.McpServerBindings = template.DefaultMcpServers;
        state.State.IsInitialized = true;
        state.State.Status = AgentStatus.Idle;

        await state.WriteStateAsync();
        logger.LogInformation("Agent {Id} ({Role}) initialized for flow run {FlowRunId}",
            state.State.Id, state.State.Role, state.State.FlowRunId);
    }

    public async Task<TaskResult> ExecuteTaskAsync(TaskInput input, GrainCancellationToken ct)
    {
        state.State.Status = AgentStatus.Running;
        await state.WriteStateAsync();

        var taskId = Guid.NewGuid();
        logger.LogInformation("Agent {Id} ({Role}) starting task {TaskId} on node {NodeId}",
            state.State.Id, state.State.Role, taskId, input.FlowNodeId);

        try
        {
            // Load memory context
            var memoryChunks = await memoryStore.SearchAsync(
                state.State.ProjectId,
                input.Payload.ToJsonString(),
                topK: 5,
                filter: new MemoryFilter { AgentId = state.State.Id });

            // Build tools from effective skills + built-in tools
            var tools = await BuildToolsAsync(input, ct.CancellationToken);

            // Build system prompt with memory context
            var systemPrompt = BuildSystemPrompt(memoryChunks);

            // Initialize working memory
            _workingMemory.Clear();
            _workingMemory.Add(new ChatMessage(ChatRole.System, systemPrompt));
            _workingMemory.Add(new ChatMessage(ChatRole.User,
                $"Task: {input.Payload.ToJsonString()}\nNode: {input.FlowNodeId}"));

            var chatClient = llmProvider.GetChatClient(state.State.Model);
            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = 4096,
                Temperature = (float?)0.7f,
                Tools = tools
            };

            JsonObject? finalOutput = null;
            var totalTokens = 0;
            var maxIterations = 10;

            for (var i = 0; i < maxIterations; i++)
            {
                var response = await chatClient.GetResponseAsync(_workingMemory, chatOptions, ct.CancellationToken);
                totalTokens += (int)(response.Usage?.TotalTokenCount ?? 0);
                state.State.TokensUsed += (int)(response.Usage?.TotalTokenCount ?? 0);

                var message = response.Messages.LastOrDefault()
                    ?? new ChatMessage(ChatRole.Assistant, string.Empty);
                _workingMemory.Add(message);

                // No tool calls = final answer
                if (response.FinishReason == ChatFinishReason.Stop || !message.Contents.OfType<FunctionCallContent>().Any())
                {
                    finalOutput = new JsonObject { ["result"] = message.Text ?? "Task completed." };
                    break;
                }

                // Handle tool calls — collect all results as FunctionResultContent
                var resultContents = new List<AIContent>();
                foreach (var toolCall in message.Contents.OfType<FunctionCallContent>())
                {
                    var toolResult = await HandleToolCallAsync(toolCall, input, ct.CancellationToken);
                    resultContents.Add(new FunctionResultContent(toolCall.CallId!, toolResult));
                }
                _workingMemory.Add(new ChatMessage(ChatRole.Tool, resultContents));

                // Check token budget
                if (state.State.TokensUsed > 100_000)
                {
                    await memoryStore.SummarizeAndPruneAsync(state.State.ProjectId, 50_000, ct.CancellationToken);
                }
            }

            finalOutput ??= new JsonObject { ["result"] = "Task completed (max iterations reached)." };

            // Store result in memory
            await memoryStore.StoreAsync(new MemoryEntry
            {
                ProjectId = state.State.ProjectId,
                AgentId = state.State.Id,
                Content = $"Task '{input.FlowNodeId}' result: {finalOutput.ToJsonString()}",
                ContentType = MemoryContentType.Decision,
                Importance = 0.7f,
                Tags = [state.State.Role, input.FlowNodeId]
            }, ct.CancellationToken);

            state.State.Status = AgentStatus.Idle;
            await state.WriteStateAsync();

            return new TaskResult
            {
                TaskId = taskId,
                Success = true,
                Output = finalOutput,
                TokensUsed = totalTokens
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent {Id} task {TaskId} failed", state.State.Id, taskId);
            state.State.Status = AgentStatus.Idle;
            await state.WriteStateAsync();

            return new TaskResult
            {
                TaskId = taskId,
                Success = false,
                Error = ex.Message
            };
        }
    }

    public Task<AgentStatus> GetStatusAsync() => Task.FromResult(state.State.Status);

    public async Task SuspendAsync(string reason)
    {
        state.State.Status = AgentStatus.Suspended;
        await state.WriteStateAsync();
        logger.LogInformation("Agent {Id} suspended: {Reason}", state.State.Id, reason);
    }

    public async Task ResumeAsync(HitlDecision decision)
    {
        _hitlTcs?.TrySetResult(decision);
        state.State.Status = AgentStatus.Running;
        await state.WriteStateAsync();
        logger.LogInformation("Agent {Id} resumed with decision: {Decision}", state.State.Id, decision.Decision);
    }

    public async Task TerminateAsync()
    {
        state.State.Status = AgentStatus.Terminated;
        await state.WriteStateAsync();
        DeactivateOnIdle();
        logger.LogInformation("Agent {Id} terminated", state.State.Id);
    }

    public async Task<IAgentGrain> SpawnSubAgentAsync(Guid templateId, string? role = null)
    {
        if (state.State.CurrentSubAgentCount >= state.State.MaxSubAgents)
            throw new InvalidOperationException(
                $"Agent {state.State.Id} has reached max sub-agents ({state.State.MaxSubAgents})");

        var subAgentId = Guid.NewGuid();
        var subAgent = GrainFactory.GetGrain<IAgentGrain>(subAgentId);

        await subAgent.InitializeAsync(new AgentInitRequest
        {
            TemplateId = templateId,
            FlowRunId = state.State.FlowRunId,
            ProjectId = state.State.ProjectId,
            ParentAgentId = state.State.Id,
            MaxSubAgents = Math.Max(1, state.State.MaxSubAgents / 2)
        });

        state.State.SubAgentIds.Add(subAgentId);
        state.State.CurrentSubAgentCount++;
        await state.WriteStateAsync();

        logger.LogInformation("Agent {Id} spawned sub-agent {SubId}", state.State.Id, subAgentId);
        return subAgent;
    }

    public Task<int> GetSubAgentCountAsync() => Task.FromResult(state.State.CurrentSubAgentCount);

    public async Task ReceiveMessageAsync(AcpMessage message)
    {
        logger.LogDebug("Agent {Id} received ACP message: {Topic}", state.State.Id, message.Topic);

        if (message.Topic == AcpTopics.HitlDecision)
        {
            var decision = JsonSerializer.Deserialize<HitlDecision>(message.Payload.ToJsonString());
            if (decision is not null) _hitlTcs?.TrySetResult(decision);
        }
    }

    public async Task<List<MemoryChunk>> QueryMemoryAsync(string query, int topK = 5)
    {
        return await memoryStore.SearchAsync(state.State.ProjectId, query, topK,
            new MemoryFilter { AgentId = state.State.Id });
    }

    public async Task<List<Skill>> GetEffectiveSkillsAsync()
    {
        return await skillRegistry.GetEffectiveSkillsAsync(state.State.Id);
    }

    public Task<AgentInfo> GetInfoAsync() => Task.FromResult(new AgentInfo
    {
        Id = state.State.Id,
        Name = state.State.Name,
        Role = state.State.Role,
        Status = state.State.Status,
        TokensUsed = state.State.TokensUsed,
        MaxSubAgents = state.State.MaxSubAgents,
        CurrentSubAgentCount = state.State.CurrentSubAgentCount,
        Model = state.State.Model,
        FlowRunId = state.State.FlowRunId,
        ParentAgentId = state.State.ParentAgentId
    });

    private async Task<IList<AITool>> BuildToolsAsync(TaskInput input, CancellationToken ct)
    {
        var tools = new List<AITool>();

        // Skill-based tools (from registry)
        var skills = await skillRegistry.GetEffectiveSkillsAsync(state.State.Id);
        foreach (var skill in skills.Where(s => s.Status == SkillStatus.Active))
        {
            var captured = skill;
            tools.Add(AIFunctionFactory.Create(
                (string args = "") => Task.FromResult($"Executed skill '{captured.Slug}': {captured.Definition.ToJsonString()}"),
                name: captured.Slug.Replace('-', '_'),
                description: captured.Description ?? captured.Name));
        }

        // Built-in: request_human_review
        tools.Add(AIFunctionFactory.Create(
            (string reason = "needs review") => Task.FromResult("HITL checkpoint enqueued"),
            name: "request_human_review",
            description: "Request a human to review and approve the current work before proceeding"));

        // Built-in: spawn_subagent
        tools.Add(AIFunctionFactory.Create(
            (string templateId = "", string subtask = "") => Task.FromResult("Sub-agent spawned"),
            name: "spawn_subagent",
            description: "Spawn a sub-agent with a specific role to handle a parallel subtask"));

        return tools;
    }

    private string BuildSystemPrompt(List<MemoryChunk> memoryChunks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(state.State.SystemPrompt);
        sb.AppendLine();

        if (memoryChunks.Count > 0)
        {
            sb.AppendLine("## Project Memory Context");
            foreach (var chunk in memoryChunks.Take(5))
            {
                sb.AppendLine($"[{chunk.ContentType}] {chunk.Content}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private async Task<string> HandleToolCallAsync(FunctionCallContent toolCall, TaskInput input, CancellationToken ct)
    {
        logger.LogDebug("Agent {Id} calling tool: {Tool}", state.State.Id, toolCall.Name);

        try
        {
            // Slash command handling
            if (toolCall.Name.StartsWith('/') || toolCall.Name == "slash_command")
            {
                var slug = toolCall.Name.TrimStart('/');
                var skill = await skillRegistry.ResolveAsync(slug, state.State.Id, ct: ct);
                if (skill is not null)
                {
                    return $"Executed skill '{skill.Name}': {skill.Definition.ToJsonString()}";
                }
                return $"Skill '{slug}' not found.";
            }

            // HITL request
            if (toolCall.Name == "request_human_review")
            {
                _hitlTcs = new TaskCompletionSource<HitlDecision>();
                state.State.Status = AgentStatus.WaitingForHitl;
                await state.WriteStateAsync();

                var checkpointId = await hitlQueue.EnqueueAsync(new HitlCheckpoint
                {
                    FlowRunId = state.State.FlowRunId,
                    FlowNodeId = input.FlowNodeId,
                    Type = CheckpointType.Review,
                    Title = $"Agent {state.State.Role} requests review",
                    Context = new JsonObject
                    {
                        ["agentId"] = state.State.Id.ToString(),
                        ["toolArgs"] = JsonSerializer.Serialize(toolCall.Arguments)
                    }
                }, ct);

                var decision = await _hitlTcs.Task.WaitAsync(ct);
                return $"Human decision: {decision.Decision}";
            }

            return $"Tool '{toolCall.Name}' result: stub response";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool call {Tool} failed", toolCall.Name);
            return $"Error executing {toolCall.Name}: {ex.Message}";
        }
    }
}

// Interface for template resolution in grain context
public interface IAgentTemplateResolver
{
    Task<Domain.Agents.AgentTemplate> ResolveAsync(Guid templateId);
}
