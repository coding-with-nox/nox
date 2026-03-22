namespace Nox.Api.Auth;

/// <summary>
/// Authorization policy names and role constants for the Nox API.
/// Roles are sourced from Keycloak realm_access.roles in the JWT.
/// </summary>
public static class NoxPolicies
{
    // Role names (must match Keycloak realm roles)
    public const string RoleAdmin   = "nox-admin";
    public const string RoleManager = "nox-manager";
    public const string RoleViewer  = "nox-viewer";

    // Policy names
    public const string AnyUser      = "NoxAnyUser";       // viewer, manager, admin
    public const string ManagerOrAdmin = "NoxManagerOrAdmin"; // manager, admin
    public const string AdminOnly    = "NoxAdminOnly";     // admin only
}
