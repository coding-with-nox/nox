using Microsoft.EntityFrameworkCore;
using Nox.Dashboard.Components;
using Nox.Domain;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using Nox.Domain.Skills;
using Nox.Infrastructure;
using Nox.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add Razor components with interactive server rendering
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Infrastructure (read-only access to DB for dashboard)
builder.Services.AddNoxInfrastructure(builder.Configuration);

// Flow engine (HTTP client to Nox.Api)
builder.Services.AddScoped<IFlowEngine, DashboardFlowEngineProxy>();

builder.Services.AddSignalR();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Dashboard uses HTTP to call Nox.Api for flow operations
public class DashboardFlowEngineProxy(IHttpClientFactory http) : IFlowEngine
{
    public async Task<FlowRun> StartAsync(Guid flowId, System.Text.Json.Nodes.JsonObject? variables = null, CancellationToken ct = default)
    {
        var client = http.CreateClient("NoxApi");
        var resp = await client.PostAsJsonAsync($"/api/flows/{flowId}/runs", new { variables }, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<FlowRun>(cancellationToken: ct))!;
    }

    public Task<FlowRun?> GetRunAsync(Guid flowRunId) => Task.FromResult<FlowRun?>(null);
    public Task PauseAsync(Guid flowRunId, string reason) => Task.CompletedTask;
    public Task ResumeAsync(Guid flowRunId, HitlDecision decision) => Task.CompletedTask;
    public Task CancelAsync(Guid flowRunId, string reason) => Task.CompletedTask;
    public IAsyncEnumerable<FlowEvent> SubscribeToEventsAsync(Guid flowRunId, CancellationToken ct)
        => throw new NotSupportedException();
}
