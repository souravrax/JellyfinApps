using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.WebApps.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WebApps.Controllers;

/// <summary>
/// Server-side truth for per-user app access (PLAN §6).
/// Jellyfin's own auth stack validates the caller's token (`[Authorize]`);
/// we only map the manifest access block onto that identity. The client-side
/// guard calls this before booting an app — a bare browser navigation carries
/// no token, so the verdict MUST be fetched with the stored token rather than
/// decided by the static middleware.
/// </summary>
[ApiController]
[Route("WebApps/Access")]
[Authorize]
public sealed class AccessController : ControllerBase
{
    private readonly AppRegistry _registry;
    private readonly IUserManager _userManager;
    private readonly ILogger<AccessController> _logger;

    public AccessController(AppRegistry registry, IUserManager userManager, ILogger<AccessController> logger)
    {
        _registry = registry;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Verdict for the calling user against an app's access block.</summary>
    /// <response code="200">Verdict (allowed may still be false — check the body).</response>
    /// <response code="404">Unknown app id.</response>
    [HttpGet("{appId}")]
    [ProducesResponseType(typeof(AccessVerdict), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AccessVerdict> Get(string appId)
    {
        var app = _registry.TryResolve(appId);
        if (app is null)
        {
            return NotFound();
        }

        var user = ResolveUser(User);
        if (user is null)
        {
            _logger.LogWarning(
                "Access verdict: could not map request to a user (claims: {Claims})",
                string.Join(",", User.Claims.Select(c => c.Type)));
            return Unauthorized();
        }

        var isAdmin = IsAdministrator(user);
        var verdict = AccessPolicy.Evaluate(
            app.Manifest,
            isAdmin,
            user.Id,
            user.Username ?? string.Empty);

        return Ok(new AccessVerdict(
            app.Manifest.Id,
            app.Manifest.Version,
            string.IsNullOrWhiteSpace(app.Manifest.Navigation.Title) ? app.Manifest.Name : app.Manifest.Navigation.Title,
            verdict.Allowed,
            verdict.Reason,
            isAdmin));
    }

    /// <summary>JSON verdict consumed by the login/access guard.</summary>
    public sealed record AccessVerdict(
        string AppId,
        string Version,
        string AppName,
        bool Allowed,
        string Reason,
        bool IsAdmin);

    /// <summary>Administrator check against v12 permission rows.</summary>
    private static bool IsAdministrator(User user) =>
        user.Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value);

    /// <summary>
    /// Maps Jellyfin's auth claims to a user without referencing the
    /// (non-NuGet-published) Jellyfin.Api assembly: id claims first
    /// (N or D guid format), username as fallback.
    /// </summary>
    private User? ResolveUser(ClaimsPrincipal principal)
    {
        var idValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("UserId")?.Value
            ?? principal.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(idValue) && Guid.TryParse(idValue.Trim(), out var id))
        {
            return _userManager.GetUserById(id);
        }

        var name = principal.Identity?.Name
            ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return _userManager.GetUserByName(name);
        }

        return null;
    }
}
