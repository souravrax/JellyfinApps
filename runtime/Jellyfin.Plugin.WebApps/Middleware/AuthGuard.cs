namespace Jellyfin.Plugin.WebApps.Middleware;

/// <summary>
/// Client-side login/access guard (PLAN §5–§6).
///
/// Why client-side: a plain browser navigation (typing a URL, following a
/// link, loading a script or stylesheet) carries NO Jellyfin token — the
/// token lives in `localStorage` and is only attached by JS `fetch` calls.
/// So the server literally cannot tell "logged-in tab" from "anonymous tab"
/// on a bare GET. But `/web/apps/*` is same-origin with jellyfin-web, so a
/// boot script can read the exact credentials jellyfin-web stored, bounce
/// anonymous visits to the real login page, and fetch the server-side access
/// verdict with the stored token. No per-app login code, ever.
///
/// Enforcement split (honest): `enabled:false` is enforced in the middleware
/// (needs no identity). `adminOnly`/`allowedUsers` are decided by the
/// `[Authorize]` verdict endpoint (Jellyfin validates the token) and enforced
/// on the app shell by this guard. Real data protection stays where it
/// belongs: the Jellyfin API, which rejects unauthenticated calls regardless
/// of what static files were served (PLAN §6 honesty clause). Static assets
/// (js/css/img) are intentionally NOT gated — inert without an API token.
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
    /// Builds the guard `&lt;script&gt;` tag. Flow: stored token? No → login.
    /// Yes → fetch the server verdict (<c>/WebApps/Access/&lt;id&gt;</c>):
    /// 401 → login; denied → blocked screen; allowed → consent screen on
    /// first visit per app+version (honesty clause), then the app boots.
    /// Launcher (no app id in path) gets the login check only.
    /// </summary>
    public static string BuildScriptTag()
    {
        // Dependency-free, ES5-ish: runs before any framework code.
        var js = "(function(){"
            + "var L=" + Json(LoginPath) + ";"
            + "var K=" + Json(CredentialsKey) + ";"
            + "var R=" + Json(ReturnKey) + ";"
            + "function toLogin(){try{location.replace(L);}catch(e){location.href=L;}}"
            + "function readCreds(){var raw=null;try{raw=localStorage.getItem(K);}catch(e){return null;}if(!raw){return null;}try{return JSON.parse(raw);}catch(e){return null;}}"
            + "function tokenOf(c){if(!c){return null;}if(c.AccessToken){return c.AccessToken;}if(c.Servers){for(var i=0;i<c.Servers.length;i++){var s=c.Servers[i];if(s&&s.AccessToken){return s.AccessToken;}}}return null;}"
            + "function appIdOf(){var m=/^\\/web\\/apps\\/([^\\/]+)\\//.exec(location.pathname);return m?decodeURIComponent(m[1]):null;}"
            + "function esc(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/\"/g,'&quot;');}"
            + "function page(t,b){document.open();document.write('<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>'+esc(t)+'</title><style>body{font-family:system-ui,sans-serif;margin:2rem;max-width:42rem}button{font-size:1rem;padding:.5rem 1rem}</style></head><body>'+b+'</body></html>');document.close();}"
            + "function blocked(r){var m={'disabled':'This app is disabled by the server administrator.','adminOnly':'This app is restricted to Jellyfin administrators.','notAllowed':'Your user is not on this app\\u2019s allow list.','verify':'Could not verify access with the server. If this keeps happening the plugin may need an update.'};page('Blocked \\u2013 Jellyfin Apps','<h1>App unavailable</h1><p>'+esc(m[r]||m['verify'])+'</p><p><a href=\"/web/apps/\">Back to apps</a></p>');}"
            + "function consent(v,ck){page(v.appName+' \\u2013 Jellyfin Apps','<h1>'+esc(v.appName)+'</h1><p><b>'+esc(v.appName)+'</b> (v'+esc(v.version||'')+') acts with your <b>full Jellyfin rights</b> \\u2014 the same as you at the API directly. Install only what you trust.</p><p><button id=\"jfok\">Continue</button> <a href=\"/web/apps/\">Cancel</a></p>');var b=document.getElementById('jfok');if(b){b.onclick=function(){try{localStorage.setItem(ck,'1');}catch(e){}location.reload();};}}"
            + "try{"
            + "var t=tokenOf(readCreds());"
            + "if(!t){try{sessionStorage.setItem(R,location.href);}catch(e){}toLogin();return;}"
            + "var id=appIdOf();if(!id){return;}"
            + "fetch('/WebApps/Access/'+encodeURIComponent(id),{headers:{'X-Emby-Token':t}})"
            + ".then(function(r){if(r.status===401){toLogin();return null;}if(!r.ok){throw new Error('http'+r.status);}return r.json();})"
            + ".then(function(v){if(!v){return;}if(!v.allowed){blocked(v.reason);return;}var ck='jfapp_consent_'+v.appId+'_'+(v.version||'0');var has=false;try{has=!!localStorage.getItem(ck);}catch(e){}if(has){return;}consent(v,ck);})"
            + ".catch(function(){blocked('verify');});"
            + "}catch(e){toLogin();}"
            + "})();";
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
