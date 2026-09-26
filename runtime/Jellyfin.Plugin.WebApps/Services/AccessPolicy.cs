using Jellyfin.Plugin.WebApps.Models;

namespace Jellyfin.Plugin.WebApps.Services;

/// <summary>
/// Pure verdict logic for the manifest access block (PLAN §6).
/// Kept free of server types so it is fully unit testable; the
/// <c>AccessController</c> supplies identity, the guard enforces the shell,
/// and the middleware enforces the kill-switch.
/// </summary>
public static class AccessPolicy
{
    public const string ReasonOk = "ok";
    public const string ReasonDisabled = "disabled";
    public const string ReasonAdminOnly = "adminOnly";
    public const string ReasonNotAllowed = "notAllowed";

    public sealed record Verdict(bool Allowed, string Reason);

    /// <param name="isAdmin">Caller has Jellyfin admin rights.</param>
    /// <param name="userId">Caller user id.</param>
    /// <param name="username">Caller username (for human-friendly allow entries).</param>
    public static Verdict Evaluate(AppManifest manifest, bool isAdmin, Guid userId, string username)
    {
        if (!manifest.Access.Enabled)
        {
            return new Verdict(false, ReasonDisabled);
        }

        if (manifest.Access.AdminOnly && !isAdmin)
        {
            return new Verdict(false, ReasonAdminOnly);
        }

        var allowed = manifest.Access.AllowedUsers;
        if (allowed.Count > 0 && !allowed.Any(entry => MatchesUser(entry, userId, username)))
        {
            return new Verdict(false, ReasonNotAllowed);
        }

        return new Verdict(true, ReasonOk);
    }

    private static bool MatchesUser(string entry, Guid userId, string username)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        entry = entry.Trim();
        if (string.Equals(entry, username, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Accept both Guid formats ("D" with dashes, "N" without).
        return Guid.TryParse(entry, out var parsed) && parsed.Equals(userId);
    }
}
