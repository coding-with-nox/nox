using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Nox.Api.Auth;
using Nox.Api.Hubs;
using Nox.Api.Middleware;
using Nox.Domain.Agents;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using Nox.Application;
using Nox.Infrastructure;
using Nox.Infrastructure.Persistence;
using Nox.Orleans;
using Nox.Orleans.GrainInterfaces;
using Nox.Orleans.Grains;
using Nox.Api.Logging;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Orleans", LogEventLevel.Warning)
    .Destructure.With<PiiScrubberPolicy>()   // GDPR: mask emails in log messages
    .WriteTo.Console()
    .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Limit request body size (prevent DoS via large payloads)
    builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1 * 1024 * 1024); // 1 MB

    // Infrastructure (EF, Redis, Qdrant, LLM)
    builder.Services.AddNoxInfrastructure(builder.Configuration);

    // Orleans Silo (co-hosted)
    builder.Host.AddNoxOrleans(builder.Configuration);

    // Application layer (use cases)
    builder.Services.AddNoxApplication();

    // Flow engine + grain resolvers
    builder.Services.AddScoped<IFlowEngine, OrleansFlowEngine>();
    builder.Services.AddScoped<IFlowResolver, DbFlowResolver>();
    builder.Services.AddScoped<IAgentTemplateResolver, DbAgentTemplateResolver>();

    // MCP server (exposes Nox as MCP tool provider — HTTP transport requires ModelContextProtocol 1.x+)
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly(typeof(Nox.McpServer.Tools.FlowTools).Assembly);

    builder.Services.AddScoped<Nox.McpServer.Tools.IFlowRepository, EfFlowRepository>();
    builder.Services.AddScoped<Nox.McpServer.Tools.IAgentTemplateRepository, EfAgentTemplateRepository>();

    // ── Authentication — Keycloak JWT Bearer ────────────────────────────────
    var authConfig = builder.Configuration.GetSection("Nox:Auth");
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authConfig["Authority"] ?? "http://localhost:8080/realms/nox";
            options.Audience  = authConfig["Audience"]  ?? "nox-api";
            options.RequireHttpsMetadata = bool.Parse(authConfig["RequireHttpsMetadata"] ?? "false");
            options.TokenValidationParameters.ValidateAudience = true;
        })
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(ApiKeyAuthHandler.SchemeName, null);

    // Extract Keycloak realm_access.roles → ClaimTypes.Role
    builder.Services.AddSingleton<IClaimsTransformation, KeycloakRolesTransformer>();

    // ── Authorization — RBAC policies ────────────────────────────────────────
    builder.Services.AddAuthorization(options =>
    {
        var bothSchemes = new[] { JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthHandler.SchemeName };

        options.AddPolicy(NoxPolicies.AnyUser, p => p
            .AddAuthenticationSchemes(bothSchemes)
            .RequireRole(NoxPolicies.RoleViewer, NoxPolicies.RoleManager, NoxPolicies.RoleAdmin));

        options.AddPolicy(NoxPolicies.ManagerOrAdmin, p => p
            .AddAuthenticationSchemes(bothSchemes)
            .RequireRole(NoxPolicies.RoleManager, NoxPolicies.RoleAdmin));

        options.AddPolicy(NoxPolicies.AdminOnly, p => p
            .AddAuthenticationSchemes(bothSchemes)
            .RequireRole(NoxPolicies.RoleAdmin));
    });

    // ── Rate limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1)
                }));
        options.RejectionStatusCode = 429;
    });

    // ── API ───────────────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
            o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSignalR();
    builder.Services.AddHostedService<RedisSignalRBridge>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
        c.SwaggerDoc("v1", new() { Title = "Nox Orchestration API", Version = "v1" }));

    // CORS — restrict to configured origins (not AllowAnyOrigin)
    var allowedOrigins = builder.Configuration
        .GetSection("Nox:Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5050"];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyMethod()
         .AllowAnyHeader()
         .AllowCredentials()));

    var app = builder.Build();

    // Migrate DB on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<NoxDbContext>();
        await db.Database.MigrateAsync();
    }

    // Seed SDLC agent templates, GitHub skills, and flow template
    await Nox.Api.Seed.SdlcSeed.RunAsync(app.Services);

    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseRateLimiter();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseMiddleware<AcpRoutingMiddleware>();

    app.MapControllers();
    app.MapHub<HitlHub>("/hubs/hitl");
    app.MapHub<AgentMonitorHub>("/hubs/agents");
    app.MapMcp("/mcp").RequireAuthorization(NoxPolicies.AnyUser);

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

    public async Task ResumeAsync(Guid flowRunId, HitlDecision decision)
    {
        var grain = orleans.GetGrain<IFlowGrain>(flowRunId);
        await grain.ResumeFromCheckpointAsync(decision.CheckpointId, decision);
    }

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
