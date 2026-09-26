using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.WebApps;

/// <summary>
/// Hosts independently developed web apps at <c>/web/apps/&lt;id&gt;/</c>.
/// See PLAN.md for the full design brief.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Jellyfin Apps";

    public override string Description => "Host independent web apps at /web/apps/<id>/. See PLAN.md.";

    public override Guid Id => Guid.Parse("9A3F9E2B-9D7A-4E2B-9E2B-9D7A4E2B9E2B");
}
