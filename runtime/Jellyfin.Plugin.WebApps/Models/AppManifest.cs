using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.WebApps.Models;

/// <summary>
/// Framework-neutral app manifest (PLAN §7).
/// Lives at <c>&lt;data&gt;/webapps/&lt;id&gt;/manifest.json</c>,
/// next to the built <c>dist/</c> folder.
/// </summary>
public sealed class AppManifest
{
    /// <summary>URL-safe id. Must match the directory name. e.g. "stats".</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public AppRuntime Runtime { get; set; } = new();

    public AppNavigation Navigation { get; set; } = new();

    /// <summary>Access block. Enforcement lands in Phase 2; model ships now.</summary>
    public AppAccess Access { get; set; } = new();

    private static readonly Regex IdPattern = new("^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.Compiled);

    public static bool IsValidId(string id) => !string.IsNullOrWhiteSpace(id) && IdPattern.IsMatch(id);

    /// <summary>Returns a list of validation errors; empty means valid.</summary>
    public List<string> Validate(string? expectedId = null)
    {
        var errors = new List<string>();
        if (!IsValidId(Id))
        {
            errors.Add($"id '{Id}' is invalid: use lowercase letters, digits, hyphens (1-64 chars).");
        }

        if (expectedId is not null && !string.Equals(Id, expectedId, StringComparison.Ordinal))
        {
            errors.Add($"manifest id '{Id}' does not match directory name '{expectedId}'.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("name is required.");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            errors.Add("version is required.");
        }

        if (string.IsNullOrWhiteSpace(Runtime.Entry))
        {
            errors.Add("runtime.entry is required (e.g. index.html).");
        }

        return errors;
    }

    public static AppManifest? Parse(string json, out List<string> errors)
    {
        errors = new List<string>();
        AppManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AppManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"manifest.json is not valid JSON: {ex.Message}");
            return null;
        }

        if (manifest is null)
        {
            errors.Add("manifest.json deserialized to null.");
            return null;
        }

        errors.AddRange(manifest.Validate());
        return errors.Count == 0 ? manifest : null;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

public sealed class AppRuntime
{
    /// <summary>Entry file inside dist/. Default "index.html".</summary>
    public string Entry { get; set; } = "index.html";

    /// <summary>When true, unknown paths under the app fall back to entry (SPA).</summary>
    public bool Spa { get; set; } = true;
}

public sealed class AppNavigation
{
    public string Title { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;
}

public sealed class AppAccess
{
    /// <summary>Global kill switch. Phase 2 enforces.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When true, only admins may open the app. Phase 2 enforces.</summary>
    public bool AdminOnly { get; set; }

    /// <summary>Empty = all users. Phase 2 enforces.</summary>
    public List<string> AllowedUsers { get; set; } = new();
}
