using Jellyfin.Plugin.WebApps.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.WebApps;

/// <summary>
/// Registers the <c>/web/apps/*</c> pipeline BEFORE Jellyfin's static files.
/// Proven pattern: same IStartupFilter interception the File Transformation
/// plugin uses. Ordering matters — <see cref="WebAppsMiddleware"/> calls
/// <c>next()</c> for anything outside our prefix, so Jellyfin Web is untouched.
/// </summary>
public sealed class WebAppsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return builder =>
        {
            builder.UseMiddleware<WebAppsMiddleware>();
            next(builder);
        };
    }
}
