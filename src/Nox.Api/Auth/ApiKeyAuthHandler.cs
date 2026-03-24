using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Nox.Api.Auth;

/// <summary>
/// Allows internal services (e.g. Dashboard) to authenticate with a static API key
/// instead of a Keycloak JWT. The key is validated against Nox:InternalApiKey config.
/// Grants the nox-manager role so the caller can perform write operations.
/// </summary>
public class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = configuration["Nox:InternalApiKey"];
        if (string.IsNullOrEmpty(configuredKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.Headers.TryGetValue(HeaderName, out var sentKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (sentKey != configuredKey)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "dashboard-service"),
            new Claim(ClaimTypes.Role, NoxPolicies.RoleManager),
            new Claim(ClaimTypes.Role, NoxPolicies.RoleAdmin),
        };
        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
