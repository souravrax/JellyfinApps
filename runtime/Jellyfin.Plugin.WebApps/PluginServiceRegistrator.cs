using Jellyfin.Plugin.WebApps.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WebApps;

/// <summary>
/// Exposes <see cref="AppRegistry"/> + <see cref="WebAppsStartupFilter"/>
/// to Jellyfin's DI container so the middleware runs before static files.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<AppRegistry>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<AppRegistry>>();
            var appPaths = provider.GetRequiredService<MediaBrowser.Common.Configuration.IApplicationPaths>();
            var root = Path.Combine(appPaths.DataPath, "webapps");
            Directory.CreateDirectory(root);
            return new AppRegistry(logger, root);
        });
        serviceCollection.AddSingleton<IStartupFilter, WebAppsStartupFilter>();
    }
}
