using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nox.Api.Auth;
using Nox.Domain.Gdpr;

namespace Nox.Api.Controllers;

/// <summary>
/// GDPR compliance endpoints — restricted to Admin role.
/// </summary>
[ApiController]
[Route("api/gdpr")]
[Authorize(Policy = NoxPolicies.AdminOnly)]
public class GdprController(IGdprService gdprService) : ControllerBase
{
    /// <summary>
    /// Anonymize all personal data for a given username (GDPR Art. 17 — Right to Erasure).
    /// Replaces the username with "[anonymized]" in all tables.
    /// This operation is irreversible.
    /// </summary>
    [HttpPost("anonymize/{username}")]
    public async Task<IActionResult> Anonymize(string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("Username is required.");

        var result = await gdprService.AnonymizeUserAsync(username, ct);
        return Ok(result);
    }
}
