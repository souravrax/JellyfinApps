using Jellyfin.Plugin.WebApps.Middleware;
using Jellyfin.Plugin.WebApps.Models;
using Jellyfin.Plugin.WebApps.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.WebApps.Tests;

public sealed class AppManifestTests
{
    [Theory]
    [InlineData("stats", true)]
    [InlineData("my-app-2", true)]
    [InlineData("Stats", false)]
    [InlineData("../escape", false)]
    [InlineData("", false)]
    [InlineData("a", true)]
    public void IdValidation(string id, bool expected)
    {
        Assert.Equal(expected, AppManifest.IsValidId(id));
    }

    [Fact]
    public void ValidateDetectsDirectoryMismatch()
    {
        const string json = """{"id":"other","name":"X","version":"1.0.0","runtime":{"entry":"index.html","spa":true}}""";
        var manifest = AppManifest.Parse(json, out var errors);
        Assert.NotNull(manifest); // valid on its own…
        Assert.Empty(errors);
        // …but fails when checked against the directory name (enforced by AppRegistry.TryResolve).
        Assert.NotEmpty(manifest!.Validate(expectedId: "stats"));
    }

    [Fact]
    public void ParseAcceptsPlanExample()
    {
        const string json = """
            {"id":"stats","name":"Media Statistics","version":"1.2.0",
             "description":"Statistics for your Jellyfin server",
             "runtime":{"entry":"index.html","spa":true},
             "navigation":{"title":"Statistics","icon":"analytics"},
             "access":{"enabled":true,"adminOnly":false,"allowedUsers":[]}}
            """;
        var manifest = AppManifest.Parse(json, out var errors);
        Assert.NotNull(manifest);
        Assert.Empty(errors);
        Assert.Equal("stats", manifest!.Id);
    }
}

public sealed class AppRegistryTests : IDisposable
{
    private readonly string _root;

    public AppRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "webapps-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void WriteApp(string root, string id, string? manifestId = null)
    {
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(Path.Combine(dir, "dist"));
        File.WriteAllText(
            Path.Combine(dir, "dist", "index.html"),
            "<!doctype html><p>hi</p>");
        File.WriteAllText(
            Path.Combine(dir, "manifest.json"),
            "{\"id\":\"" + (manifestId ?? id) + "\",\"name\":\"Test\",\"version\":\"0.1.0\",\"runtime\":{\"entry\":\"index.html\",\"spa\":true}}");
    }

    [Fact]
    public void ResolvesValidApp()
    {
        WriteApp(_root, "vanilla-test");
        var registry = new AppRegistry(NullLogger<AppRegistry>.Instance, _root);
        var app = registry.TryResolve("vanilla-test");
        Assert.NotNull(app);
        Assert.EndsWith("dist", app!.ContentRoot);
    }

    [Fact]
    public void RejectsTraversalIds()
    {
        var registry = new AppRegistry(NullLogger<AppRegistry>.Instance, _root);
        Assert.Null(registry.TryResolve("../escape"));
        Assert.Null(registry.TryResolve(""));
        Assert.Null(registry.TryResolve(null));
    }

    [Fact]
    public void RejectsManifestMismatch()
    {
        WriteApp(_root, "stats", manifestId: "other");
        var registry = new AppRegistry(NullLogger<AppRegistry>.Instance, _root);
        Assert.Null(registry.TryResolve("stats"));
    }

    [Fact]
    public void ContentPathCannotEscapeRoot()
    {
        WriteApp(_root, "stats");
        var registry = new AppRegistry(NullLogger<AppRegistry>.Instance, _root);
        var app = registry.TryResolve("stats");
        Assert.NotNull(app);
        Assert.Null(AppRegistry.MapContentPath(app!.ContentRoot, "../../jellyfin.db"));
        Assert.NotNull(AppRegistry.MapContentPath(app.ContentRoot, "index.html"));
    }
}

public sealed class AuthGuardTests
{
    [Fact]
    public void ScriptChecksStoredCredentialsAndRedirectsToLogin()
    {
        var tag = AuthGuard.BuildScriptTag();
        Assert.StartsWith("<script>", tag);
        Assert.EndsWith("</script>", tag);
        Assert.Contains("jellyfin_credentials", tag);
        Assert.Contains("/web/#/login", tag);
        Assert.Contains("AccessToken", tag);
        Assert.Contains("localStorage", tag);
    }

    [Fact]
    public void ScriptFetchesVerdictAndGatesOnConsent()
    {
        var tag = AuthGuard.BuildScriptTag();
        Assert.Contains("/WebApps/Access/", tag);
        Assert.Contains("X-Emby-Token", tag);
        Assert.Contains("jfapp_consent_", tag);
        Assert.Contains("full Jellyfin rights", tag);
        Assert.Contains("adminOnly", tag);
    }

    [Fact]
    public void InjectsFirstInsideHead()
    {
        const string html = "<!doctype html><html><head><title>T</title></head><body></body></html>";
        var result = AuthGuard.InjectIntoHtml(html);
        var headEnd = result.IndexOf("<head>", StringComparison.Ordinal) + "<head>".Length;
        var scriptAt = result.IndexOf("<script>", StringComparison.Ordinal);
        Assert.Equal(headEnd, scriptAt);
        Assert.Contains("<title>T</title>", result);
    }

    [Fact]
    public void PrependsWhenNoHead()
    {
        var result = AuthGuard.InjectIntoHtml("<p>hi</p>");
        Assert.StartsWith("<script>", result);
        Assert.EndsWith("<p>hi</p>", result);
    }
}

public sealed class AccessPolicyTests
{
    private static readonly Guid UserId = Guid.Parse("b07a2333-50a7-496d-9243-44d658a4a3b9");

    private static AppManifest Manifest(bool enabled = true, bool adminOnly = false, params string[] allowedUsers) =>
        new()
        {
            Id = "stats",
            Name = "Stats",
            Version = "1.0.0",
            Access = new AppAccess { Enabled = enabled, AdminOnly = adminOnly, AllowedUsers = allowedUsers.ToList() },
        };

    [Fact]
    public void OpenAppAllowsEveryone()
    {
        Assert.Equal(new AccessPolicy.Verdict(true, "ok"), AccessPolicy.Evaluate(Manifest(), false, UserId, "sourav"));
    }

    [Fact]
    public void DisabledDeniesEvenAdmins()
    {
        Assert.Equal(new AccessPolicy.Verdict(false, "disabled"), AccessPolicy.Evaluate(Manifest(enabled: false), true, UserId, "sourav"));
    }

    [Fact]
    public void AdminOnlyDeniesRegularUsers()
    {
        Assert.Equal(new AccessPolicy.Verdict(false, "adminOnly"), AccessPolicy.Evaluate(Manifest(adminOnly: true), false, UserId, "sourav"));
        Assert.Equal(new AccessPolicy.Verdict(true, "ok"), AccessPolicy.Evaluate(Manifest(adminOnly: true), true, UserId, "sourav"));
    }

    [Theory]
    [InlineData("sourav", true)] // exact username
    [InlineData("SOURAV", true)] // case-insensitive username
    [InlineData("b07a2333-50a7-496d-9243-44d658a4a3b9", true)] // id, dashed
    [InlineData("b07a233350a7496d924344d658a4a3b9", true)] // id, compact
    [InlineData("someone-else", false)]
    public void AllowListMatchesNameOrId(string entry, bool allowed)
    {
        var manifest = Manifest(allowedUsers: new[] { entry });
        var verdict = AccessPolicy.Evaluate(manifest, false, UserId, "sourav");
        Assert.Equal(allowed, verdict.Allowed);
        Assert.Equal(allowed ? "ok" : "notAllowed", verdict.Reason);
    }
}
