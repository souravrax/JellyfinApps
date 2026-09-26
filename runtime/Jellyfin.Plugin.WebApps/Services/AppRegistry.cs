using Jellyfin.Plugin.WebApps.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WebApps.Services;

/// <summary>
/// Resolves app id → manifest → content root.
/// Content root layout: <c>&lt;data&gt;/webapps/&lt;id&gt;/dist/</c>
/// with <c>manifest.json</c> sitting next to <c>dist/</c>.
/// Invalid apps are skipped (logged) — one bad app never breaks the rest.
/// </summary>
public sealed class AppRegistry
{
    private readonly ILogger<AppRegistry> _logger;
    private readonly string _appsRoot;

    public AppRegistry(ILogger<AppRegistry> logger, string appsRoot)
    {
        _logger = logger;
        _appsRoot = Path.GetFullPath(appsRoot);
    }

    public string AppsRoot => _appsRoot;

    public sealed record ResolvedApp(AppManifest Manifest, string AppDir, string ContentRoot);

    public IReadOnlyList<ResolvedApp> ListApps()
    {
        var result = new List<ResolvedApp>();
        if (!Directory.Exists(_appsRoot))
        {
            return result;
        }

        foreach (var dir in Directory.GetDirectories(_appsRoot))
        {
            var app = TryResolve(Path.GetFileName(dir));
            if (app is not null)
            {
                result.Add(app);
            }
        }

        return result.OrderBy(a => a.Manifest.Id, StringComparer.Ordinal).ToList();
    }

    public ResolvedApp? TryResolve(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || !AppManifest.IsValidId(appId))
        {
            return null;
        }

        // Confine to apps root (no traversal even if caller passes "../x").
        var appDir = Path.GetFullPath(Path.Combine(_appsRoot, appId));
        if (!appDir.StartsWith(_appsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(appDir, _appsRoot, StringComparison.Ordinal))
        {
            _logger.LogWarning("App id {AppId} escapes apps root, ignoring", appId);
            return null;
        }

        var manifestPath = Path.Combine(appDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        AppManifest? manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = AppManifest.Parse(json, out var errors);
            if (manifest is null)
            {
                _logger.LogWarning("Invalid manifest for app {AppId}: {Errors}", appId, string.Join("; ", errors));
                return null;
            }

            if (!string.Equals(manifest.Id, appId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Manifest id {ManifestId} does not match directory {Dir}, ignoring",
                    manifest.Id,
                    appId);
                return null;
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read manifest for app {AppId}", appId);
            return null;
        }

        var contentRoot = Path.GetFullPath(Path.Combine(appDir, "dist"));
        if (!contentRoot.StartsWith(appDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        if (!Directory.Exists(contentRoot))
        {
            _logger.LogWarning("App {AppId} has no dist/ folder yet", appId);
            return null;
        }

        return new ResolvedApp(manifest, appDir, contentRoot);
    }

    /// <summary>
    /// Maps a URL remainder (after "/web/apps/&lt;id&gt;") to a file inside the
    /// content root. Returns null when the path would escape the root.
    /// </summary>
    public static string? MapContentPath(string contentRoot, string remainder)
    {
        var relative = remainder.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(relative))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(contentRoot, relative));
        if (!full.StartsWith(contentRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, contentRoot, StringComparison.Ordinal))
        {
            return null;
        }

        return full;
    }
}
