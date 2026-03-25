using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Nox.Dashboard.Components;
using Nox.Dashboard.Services;
using Nox.Domain;
using Nox.Domain.Flows;
using Nox.Domain.Hitl;
using Nox.Domain.Skills;
using Nox.Infrastructure;
using Nox.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ── Data Protection ────────────────────────────────────────────────
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new System.IO.DirectoryInfo("/app/dataprotection-keys"))
        .SetApplicationName("nox-dashboard");

    // ── Authentication: Cookie + Keycloak OIDC ─────────────────────────
    var publicAuthority   = builder.Configuration["Nox:Auth:PublicAuthority"]
                            ?? "http://localhost:8080/realms/nox";
    var internalAuthority = builder.Configuration["Nox:Auth:InternalAuthority"]
                            ?? publicAuthority;

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name       = "nox.auth";
        options.Cookie.HttpOnly   = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite   = SameSiteMode.Lax;
        options.ExpireTimeSpan    = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect(options =>
    {
        var publicMetadataAddress = publicAuthority.TrimEnd('/') + "/.well-known/openid-configuration";

        // Authority = public URL used by the browser during redirects
        options.Authority           = publicAuthority;
        options.MetadataAddress     = publicMetadataAddress;
        options.ClientId            = "nox-dashboard";
        options.ResponseType        = "code";
        options.UsePkce            = true;
        options.SaveTokens          = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.BackchannelHttpHandler = new OpenIdConnectBackchannelRewriteHandler(publicAuthority, internalAuthority)
        {
            InnerHandler = new HttpClientHandler()
        };

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("roles");

        // Map Keycloak realm_access.roles → ClaimTypes.Role
        options.ClaimActions.MapJsonSubKey(ClaimTypes.Role, "realm_access", "roles");

        options.TokenValidationParameters = new()
        {
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role,
            // Accept tokens issued by either the public or internal authority URL
            ValidIssuers  = [publicAuthority, internalAuthority],
        };
    });

    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    // ── Razor components ───────────────────────────────────────────────
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // ── Infrastructure (read-only DB access) ──────────────────────────
    builder.Services.AddNoxInfrastructure(builder.Configuration);

    // ── HTTP client to Nox.Api ─────────────────────────────────────────
    builder.Services.AddHttpClient("NoxApi", client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Nox:ApiBaseUrl"] ?? "http://localhost:5000");
        var apiKey = builder.Configuration["Nox:InternalApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    });

    builder.Services.AddScoped<IFlowEngine, DashboardFlowEngineProxy>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSignalR();
    builder.Services.AddScoped<ThemeService>();
    builder.Services.AddScoped<LanguageService>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // ── HTTP Security Headers (F08) ───────────────────────────────────────────
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Frame-Options"]        = "DENY";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Referrer-Policy"]        = "strict-origin-when-cross-origin";
        ctx.Response.Headers["Permissions-Policy"]     = "camera=(), microphone=(), geolocation=()";
        await next();
    });

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    // ── OIDC challenge endpoint (called by Login.razor) ───────────────
    app.MapGet("/auth/login", (HttpContext ctx, string? returnUrl) =>
        ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = returnUrl ?? "/" }))
        .AllowAnonymous();

    // ── Logout endpoint — clears cookie + ends Keycloak session ───────
    app.MapGet("/logout", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = "/" });
    }).RequireAuthorization();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Nox Dashboard failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// ── Dashboard flow engine proxy ────────────────────────────────────────
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

file sealed class OpenIdConnectBackchannelRewriteHandler(string publicAuthority, string internalAuthority) : DelegatingHandler
{
    private readonly Uri _publicBase = EnsureTrailingSlash(publicAuthority);
    private readonly Uri _internalBase = EnsureTrailingSlash(internalAuthority);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && ShouldRewrite(uri))
        {
            var relative = _publicBase.MakeRelativeUri(uri);
            request.RequestUri = new Uri(_internalBase, relative);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private bool ShouldRewrite(Uri requestUri) =>
        _publicBase != _internalBase
        && string.Equals(requestUri.Scheme, _publicBase.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(requestUri.Host, _publicBase.Host, StringComparison.OrdinalIgnoreCase)
        && requestUri.Port == _publicBase.Port;

    private static Uri EnsureTrailingSlash(string authority) =>
        new(authority.EndsWith('/') ? authority : authority + "/", UriKind.Absolute);
}
