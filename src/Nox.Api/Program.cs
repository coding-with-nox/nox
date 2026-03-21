using Microsoft.EntityFrameworkCore;
using Nox.Api.Hubs;
using Nox.Api.Middleware;
using Nox.Domain.Agents;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using Nox.Infrastructure;
using Nox.Infrastructure.Persistence;
using Nox.Orleans;
using Nox.Orleans.GrainInterfaces;
using Nox.Orleans.Grains;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Orleans", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Infrastructure (EF, Redis, Qdrant, LLM)
    builder.Services.AddNoxInfrastructure(builder.Configuration);

    // Orleans Silo (co-hosted)
    builder.Host.AddNoxOrleans(builder.Configuration);

    // Flow engine + grain resolvers
    builder.Services.AddScoped<IFlowEngine, OrleansFlowEngine>();
    builder.Services.AddScoped<IFlowResolver, DbFlowResolver>();
    builder.Services.AddScoped<IAgentTemplateResolver, DbAgentTemplateResolver>();

    // MCP server (exposes Nox as MCP tool provider — HTTP transport requires ModelContextProtocol 1.x+)
    builder.Services
        .AddMcpServer()
        .WithToolsFromAssembly(typeof(Nox.McpServer.Tools.FlowTools).Assembly);

    builder.Services.AddScoped<Nox.McpServer.Tools.IFlowRepository, EfFlowRepository>();
    builder.Services.AddScoped<Nox.McpServer.Tools.IAgentTemplateRepository, EfAgentTemplateRepository>();

    // API
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
            o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

    builder.Services.AddSignalR();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
        c.SwaggerDoc("v1", new() { Title = "Nox Orchestration API", Version = "v1" }));

    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    var app = builder.Build();

    // Migrate DB on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<NoxDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseSerilogRequestLogging();
    app.UseCors();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseMiddleware<AcpRoutingMiddleware>();

    app.MapControllers();
    app.MapHub<HitlHub>("/hubs/hitl");
    app.MapHub<AgentMonitorHub>("/hubs/agents");
    // app.MapMcp("/mcp"); // requires ModelContextProtocol 1.x+

    app.MapGet("/health", () => new { status = "healthy", timestamp = DateTimeOffset.UtcNow });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Nox API failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// ---- In-process implementations ----

public class OrleansFlowEngine(
    IClusterClient orleans,
    NoxDbContext db) : IFlowEngine
{
    public async Task<FlowRun> StartAsync(Guid flowId, System.Text.Json.Nodes.JsonObject? variables = null, CancellationToken ct = default)
    {
        var flowRunId = Guid.NewGuid();
        var grain = orleans.GetGrain<IFlowGrain>(flowRunId);
        await grain.StartAsync(new FlowStartRequest
        {
            FlowId = flowId,
            ProjectId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Variables = variables?.ToJsonString() ?? "{}"
        });

        var run = new FlowRun { Id = flowRunId, FlowId = flowId, Variables = variables ?? new() };
        db.FlowRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<FlowRun?> GetRunAsync(Guid flowRunId) =>
        await db.FlowRuns.FindAsync([flowRunId]);

    public async Task PauseAsync(Guid flowRunId, string reason)
    {
        var grain = orleans.GetGrain<IFlowGrain>(flowRunId);
        await grain.CancelAsync(reason);
    }

    public Task ResumeAsync(Guid flowRunId, HitlDecision decision) =>
        Task.CompletedTask; // Resume via HitlController → FlowGrain.ResumeFromCheckpointAsync

    public async Task CancelAsync(Guid flowRunId, string reason)
    {
        var grain = orleans.GetGrain<IFlowGrain>(flowRunId);
        await grain.CancelAsync(reason);
    }

    public IAsyncEnumerable<FlowEvent> SubscribeToEventsAsync(Guid flowRunId, CancellationToken ct) =>
        throw new NotSupportedException("Use /hubs/hitl SignalR hub for real-time events");
}

public class EfFlowRepository(NoxDbContext db) : Nox.McpServer.Tools.IFlowRepository
{
    public async Task<List<Flow>> ListByProjectAsync(Guid projectId) =>
        await db.Flows.Where(f => f.ProjectId == projectId).ToListAsync();

    public async Task<Flow?> FindByNameAsync(string name) =>
        await db.Flows.FirstOrDefaultAsync(f => f.Name == name);
}

public class EfAgentTemplateRepository(NoxDbContext db) : Nox.McpServer.Tools.IAgentTemplateRepository
{
    public async Task<List<AgentTemplate>> ListAllAsync() =>
        await db.AgentTemplates.ToListAsync();
}
