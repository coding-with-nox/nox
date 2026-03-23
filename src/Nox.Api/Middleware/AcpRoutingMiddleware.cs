using Nox.Domain;
using Nox.Domain.Messages;
using Nox.Domain.Skills;
using Nox.Domain.Hitl;
using Nox.Domain.Memory;
using Nox.Orleans.GrainInterfaces;
using Orleans;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nox.Api.Middleware;

/// <summary>
/// Routes ACP messages from agents to the appropriate domain service.
/// POST /acp/message
/// </summary>
public class AcpRoutingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path != "/acp/message" || context.Request.Method != "POST")
        {
            await next(context);
            return;
        }

        // Require authenticated caller (grain-to-API or agent-to-API)
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        AcpMessage? message;
        try
        {
            message = await JsonSerializer.DeserializeAsync<AcpMessage>(
                context.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid ACP message");
            return;
        }

        if (message is null)
        {
            context.Response.StatusCode = 400;
            return;
        }

        await RouteMessageAsync(message, context);
    }

    private static async Task RouteMessageAsync(AcpMessage message, HttpContext ctx)
    {
        var sp = ctx.RequestServices;
        var logger = sp.GetRequiredService<ILogger<AcpRoutingMiddleware>>();

        logger.LogDebug("Routing ACP message: {Topic} from {From}", message.Topic, message.From?.AgentId);

        try
        {
            switch (message.Topic)
            {
                case AcpTopics.SkillPropose:
                    await HandleSkillProposeAsync(message, sp);
                    break;

                case AcpTopics.HitlRequest:
                    await HandleHitlRequestAsync(message, sp);
                    break;

                case AcpTopics.MemoryStore:
                    await HandleMemoryStoreAsync(message, sp);
                    break;

                case AcpTopics.MemoryQueryRequest:
                    await HandleMemoryQueryAsync(message, ctx, sp);
                    return; // writes response directly

                case AcpTopics.AgentSpawnRequest:
                    await HandleAgentSpawnAsync(message, ctx, sp);
                    return;

                default:
                    logger.LogWarning("Unknown ACP topic: {Topic}", message.Topic);
                    break;
            }

            ctx.Response.StatusCode = 202;
            await ctx.Response.WriteAsync("Accepted");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ACP routing error for topic {Topic}", message.Topic);
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("Internal error processing ACP message");
        }
    }

    private static async Task HandleSkillProposeAsync(AcpMessage message, IServiceProvider sp)
    {
        var skillRegistry = sp.GetRequiredService<ISkillRegistry>();
        var proposal = JsonSerializer.Deserialize<SkillProposal>(message.Payload.ToJsonString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (proposal is null || message.From is null) return;

        if (proposal.Scope == SkillScope.Personal)
            await skillRegistry.ProposePersonalSkillAsync(message.From.AgentId, proposal);
        else
            await skillRegistry.ProposeGlobalSkillAsync(message.From.AgentId, proposal);
    }

    private static async Task HandleHitlRequestAsync(AcpMessage message, IServiceProvider sp)
    {
        var hitlQueue = sp.GetRequiredService<IHitlQueue>();
        var checkpoint = JsonSerializer.Deserialize<HitlCheckpoint>(message.Payload.ToJsonString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (checkpoint is not null)
            await hitlQueue.EnqueueAsync(checkpoint);
    }

    private static async Task HandleMemoryStoreAsync(AcpMessage message, IServiceProvider sp)
    {
        var memoryStore = sp.GetRequiredService<IMemoryStore>();
        var entry = JsonSerializer.Deserialize<MemoryEntry>(message.Payload.ToJsonString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (entry is not null)
            await memoryStore.StoreAsync(entry);
    }

    private static async Task HandleMemoryQueryAsync(AcpMessage message, HttpContext ctx, IServiceProvider sp)
    {
        var memoryStore = sp.GetRequiredService<IMemoryStore>();
        var query = message.Payload["query"]?.GetValue<string>() ?? "";
        var projectId = Guid.Parse(message.Payload["projectId"]?.GetValue<string>() ?? Guid.Empty.ToString());
        var topK = Math.Clamp(message.Payload["topK"]?.GetValue<int>() ?? 5, 1, 100);

        var chunks = await memoryStore.SearchAsync(projectId, query, topK);

        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            correlationId = message.CorrelationId,
            topic = AcpTopics.MemoryQueryResponse,
            chunks
        }));
    }

    private static async Task HandleAgentSpawnAsync(AcpMessage message, HttpContext ctx, IServiceProvider sp)
    {
        var orleans = sp.GetRequiredService<IClusterClient>();
        var templateId = Guid.Parse(message.Payload["templateId"]?.GetValue<string>() ?? Guid.Empty.ToString());

        if (message.From is null) return;

        var parentGrain = orleans.GetGrain<IAgentGrain>(message.From.AgentId);
        var subAgent = await parentGrain.SpawnSubAgentAsync(templateId);
        var info = await subAgent.GetInfoAsync();

        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            correlationId = message.CorrelationId,
            topic = AcpTopics.AgentSpawnResponse,
            subAgentId = info.Id
        }));
    }
}
