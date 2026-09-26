namespace Jellyfin.Plugin.WebApps.Middleware;

/// <summary>
/// Client-side login guard (PLAN §5).
///
/// Why client-side: a plain browser navigation (typing a URL, following a
/// link, loading an `<script>`/`<link>`) carries NO Jellyfin token — the
/// token lives in `localStorage` and is only attached by JS `fetch` calls.
/// So the server literally cannot tell "logged-in tab" from "anonymous tab"
/// on a bare GET. But `/web/apps/*` is same-origin with jellyfin-web, so a
/// boot script can read the exact credentials jellyfin-web stored and bounce
/// anonymous visits to the real login page. No per-app login code, ever.
///
/// Real data protection stays where it belongs: the Jellyfin API, which
/// rejects unauthenticated calls regardless of what static files were served
/// (PLAN §6 honesty clause). Static assets (js/css/img) are intentionally NOT
/// gated — they are inert without an API token.
///
/// Selection: injected into every served `.html` document (launcher, app
/// entry, SPA fallback). Never into js/css/assets.
/// </summary>
public static class AuthGuard
{
    /// <summary>jellyfin-web login route (PLAN §5).</summary>
    public const string LoginPath = "/web/#/login";

    /// <summary>localStorage key jellyfin-web's credential provider uses (verified against the shipped bundle).</summary>
    public const string CredentialsKey = "jellyfin_credentials";

    /// <summary>sessionStorage key remembering where to return after login.</summary>
    public const string ReturnKey = "jfapp_return";

    /// <summary>
    /// Builds the guard `&lt;script&gt;` tag. Fails closed: any error (private
    /// mode, corrupt JSON, unknown shape) redirects to login. Finds a token
    /// in top-level `AccessToken` or any `Servers[].AccessToken`, then no-ops.
    /// </summary>
    public static string BuildScriptTag()
    {
        // Keep the JS dependency-free and ES5-ish (runs before any framework).
        var js = "(function(){var L=" + Json(LoginPath)
            + ";var K=" + Json(CredentialsKey)
            + ";var R=" + Json(ReturnKey)
            + ";function toLogin(){try{location.replace(L);}catch(e){location.href=L;}}"
            + "try{var raw=null;try{raw=localStorage.getItem(K);}catch(e){}"
            + "var ok=false;"
            + "if(raw){try{var c=JSON.parse(raw);"
            + "if(c){if(c.AccessToken){ok=true;}"
            + "else if(c.Servers){for(var i=0;i<c.Servers.length;i++){var s=c.Servers[i];if(s&&s.AccessToken){ok=true;break;}}}}}"
            + "catch(e){}}"
            + "if(ok){return;}"
            + "try{sessionStorage.setItem(R,location.href);}catch(e){}"
            + "toLogin();}catch(e){toLogin();}})();";
        return "<script>" + js + "</script>";
    }

    /// <summary>
    /// Inserts the guard as the first thing inside `&lt;head&gt;` so it runs
    /// before any app code. No `&lt;head&gt;` → prepends to the document.
    /// Pure function — unit tested.
    /// </summary>
    public static string InjectIntoHtml(string html)
    {
        var tag = BuildScriptTag();
        var idx = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var end = html.IndexOf('>', idx);
            if (end >= 0)
            {
                return html.Substring(0, end + 1) + tag + html.Substring(end + 1);
            }
        }

        return tag + html;
    }

    private static string Json(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
