using System.Net;
using Jellyfin.Plugin.WebApps.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.WebApps.Middleware;

/// <summary>
/// Serves <c>/web/apps/*</c> BEFORE Jellyfin's static files (registered via
/// <see cref="WebAppsStartupFilter"/>). Owns:
/// <list type="bullet">
/// <item><c>/web/apps/</c> launcher listing installed apps.</item>
/// <item><c>/web/apps/&lt;id&gt;/*</c> static files + SPA fallback to entry.</item>
/// </list>
/// Iron rule: the platform owns the mount point; each app owns its routing.
/// </summary>
public sealed class WebAppsMiddleware
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".map"] = "application/json",
        [".txt"] = "text/plain; charset=utf-8",
    };

    private readonly RequestDelegate _next;
    private readonly AppRegistry _registry;
    private readonly ILogger<WebAppsMiddleware> _logger;

    public WebAppsMiddleware(RequestDelegate next, AppRegistry registry, ILogger<WebAppsMiddleware> logger)
    {
        _next = next;
        _registry = registry;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only handle /web/apps and /web/apps/* — everything else passes through.
        if (!IsAppsPath(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Normalize: "/web/apps" → redirect to "/web/apps/".
        if (string.Equals(path, "/web/apps", StringComparison.Ordinal))
        {
            context.Response.Redirect("/web/apps/", permanent: false);
            return;
        }

        var remainder = path.Substring("/web/apps/".Length); // "" | "<id>" | "<id>/..." | "<id>/"
        if (string.IsNullOrEmpty(remainder))
        {
            await ServeLauncherAsync(context).ConfigureAwait(false);
            return;
        }

        var slash = remainder.IndexOf('/');
        var appId = slash < 0 ? remainder : remainder.Substring(0, slash);
        var appRemainder = slash < 0 ? string.Empty : remainder.Substring(slash + 1);

        var app = _registry.TryResolve(appId);
        if (app is null)
        {
            // Unknown app id: if it looks like a file-less route, still 404 here
            // so a typo never falls through into Jellyfin web statics.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync($"Unknown app '{WebUtility.HtmlEncode(appId)}'.").ConfigureAwait(false);
            return;
        }

        // Kill-switch (PLAN §6): disabled apps vanish as if uninstalled.
        // Needs no identity, so it IS enforced here in the middleware.
        if (!app.Manifest.Access.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync($"App '{WebUtility.HtmlEncode(appId)}' is disabled.").ConfigureAwait(false);
            return;
        }

        // "/web/apps/<id>" → canonical trailing slash (relative assets resolve).
        if (slash < 0)
        {
            context.Response.Redirect($"/web/apps/{appId}/", permanent: false);
            return;
        }

        // Empty remainder ("/web/apps/<id>/") → serve entry.
        if (string.IsNullOrEmpty(appRemainder))
        {
            await ServeEntryAsync(context, app).ConfigureAwait(false);
            return;
        }

        var mapped = AppRegistry.MapContentPath(app.ContentRoot, appRemainder);
        if (mapped is not null && File.Exists(mapped) && !Directory.Exists(mapped))
        {
            await ServeFileAsync(context, mapped).ConfigureAwait(false);
            return;
        }

        // SPA fallback: deep links ("/foo/bar") serve entry so the app router
        // can render them. Non-SPA apps 404 on missing files.
        if (app.Manifest.Runtime.Spa)
        {
            await ServeEntryAsync(context, app).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private async Task ServeEntryAsync(HttpContext context, AppRegistry.ResolvedApp app)
    {
        var entry = string.IsNullOrWhiteSpace(app.Manifest.Runtime.Entry)
            ? "index.html"
            : app.Manifest.Runtime.Entry.TrimStart('/');
        var mapped = AppRegistry.MapContentPath(app.ContentRoot, entry);
        if (mapped is null || !File.Exists(mapped))
        {
            _logger.LogWarning("App {AppId} is missing entry file {Entry}", app.Manifest.Id, entry);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync($"App '{app.Manifest.Id}' has no {entry} yet.").ConfigureAwait(false);
            return;
        }

        await ServeFileAsync(context, mapped).ConfigureAwait(false);
    }

    private static async Task ServeFileAsync(HttpContext context, string filePath)
    {
        var ext = Path.GetExtension(filePath);
        context.Response.ContentType = ContentTypes.TryGetValue(ext, out var ct)
            ? ct
            : "application/octet-stream";
        context.Response.Headers[HeaderNames.CacheControl] = "no-cache";

        // Login guard (PLAN §5): HTML documents get the boot-time auth check
        // injected; js/css/assets are served byte-identical (inert w/o token).
        if (string.Equals(ext, ".html", StringComparison.OrdinalIgnoreCase))
        {
            var html = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            await context.Response.WriteAsync(AuthGuard.InjectIntoHtml(html)).ConfigureAwait(false);
            return;
        }

        await context.Response.SendFileAsync(filePath).ConfigureAwait(false);
    }

    private async Task ServeLauncherAsync(HttpContext context)
    {
        // Disabled apps are hidden from the launcher (kill-switch).
        var apps = _registry.ListApps().Where(a => a.Manifest.Access.Enabled).ToList();
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(LauncherPage(apps)).ConfigureAwait(false);
    }

    private static string LauncherPage(IReadOnlyList<AppRegistry.ResolvedApp> apps)
    {
        var items = apps.Count == 0
            ? "<p>No apps installed yet. Add one under <code>&lt;data&gt;/webapps/&lt;id&gt;/</code>.</p>"
            : string.Join("\n", apps.Select(a =>
                {
                    var title = string.IsNullOrWhiteSpace(a.Manifest.Navigation.Title) ? a.Manifest.Name : a.Manifest.Navigation.Title;
                    var badge = a.Manifest.Access.AdminOnly ? " <small>[admin]</small>" : string.Empty;
                    return $"<li><a href=\"/web/apps/{WebUtility.HtmlEncode(a.Manifest.Id)}/\">{WebUtility.HtmlEncode(title)}</a>{badge} <small>{WebUtility.HtmlEncode(a.Manifest.Id)} · v{WebUtility.HtmlEncode(a.Manifest.Version)}</small></li>";
                }));

        return $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            {AuthGuard.BuildScriptTag()}
            <title>Jellyfin Apps</title></head>
            <body>
            <h1>Jellyfin Apps</h1>
            <ul>
            {items}
            </ul>
            </body>
            </html>
            """;
    }

    private static bool IsAppsPath(string path) =>
        path.Equals("/web/apps", StringComparison.Ordinal)
        || path.Equals("/web/apps/", StringComparison.Ordinal)
        || path.StartsWith("/web/apps/", StringComparison.Ordinal);
}
