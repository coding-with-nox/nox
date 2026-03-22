using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Nox.Api.Auth;

/// <summary>
/// Extracts Keycloak realm roles from the JWT "realm_access" claim
/// and injects them as standard ClaimTypes.Role claims so that
/// ASP.NET Core [Authorize(Roles = ...)] and policy checks work correctly.
/// </summary>
public sealed class KeycloakRolesTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var realmAccessClaim = principal.FindFirst("realm_access");
        if (realmAccessClaim is null) return Task.FromResult(principal);

        try
        {
            using var doc = JsonDocument.Parse(realmAccessClaim.Value);
            if (!doc.RootElement.TryGetProperty("roles", out var rolesElement))
                return Task.FromResult(principal);

            var identity = new ClaimsIdentity();
            foreach (var role in rolesElement.EnumerateArray())
            {
                var roleName = role.GetString();
                if (!string.IsNullOrWhiteSpace(roleName))
                    identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
            }

            if (identity.Claims.Any())
                principal.AddIdentity(identity);
        }
        catch (JsonException)
        {
            // Malformed claim — skip silently; auth will still fail if roles are required
        }

        return Task.FromResult(principal);
    }
}
