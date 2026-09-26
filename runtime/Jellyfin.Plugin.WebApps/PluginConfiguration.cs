using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WebApps;

/// <summary>Server-wide settings. Per-app access lives in each manifest (PLAN §6).</summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Reserved for a future global kill-switch UI. Manifest `enabled` is the per-app switch.</summary>
    public bool AllowApps { get; set; } = true;
}
