# Jellyfin Apps — Project Plan

> Start a new session with this file as the brief. It consolidates the full
> design discussion: vision, architecture, and every decision already made,
> so nothing needs re-deriving.

## 1. Vision

Turn Jellyfin into a platform for independently developed web applications.

A Jellyfin App is a normal web application — React, Svelte, Vue, Solid,
vanilla JS — hosted by the Jellyfin server with access to Jellyfin's APIs,
authentication, user context, and permissions.

Core URL convention:

```text
http://jellyfin:8096/web/apps/<app-id>/
```

Examples: `/web/apps/stats/`, `/web/apps/requests/`, `/web/apps/people/`.
Each application owns everything underneath its mount point.

## 2. Core Architecture

```text
Jellyfin Server
     │
     ├────────── /api ────────── Jellyfin APIs
     │
     ├────────── /web ────────── Jellyfin Web
     │                                │
     │                           /web/apps/* ── Apps Runtime (this project)
     │                                │
     │                ┌───────────────┼───────────────┐
     │              stats          requests        people
     │              React           Svelte          React
     │                └───────────────┼───────────────┘
     │                           App SDK (@jellyfin/apps)
     │                                │
     └────────────────────────── Jellyfin API
```

**Iron rule: Jellyfin owns the app mount point. The application owns its
routing.** Never integrate applications into Jellyfin Web's React Router.
(This constraint is load-bearing: it is what kills the entire class of
index.html-patching, fallback-hiding, header-cloning hacks.)

## 3. Routing & Base Path (decided)

Browser routers see the REAL path — `/web/apps/appid/users/123` does NOT
match `/` or `/users/:id`. Nothing about this is automatic. The platform
owns it three ways:

1. **Router base, stamped at build time** from the manifest id
   (`basename` / `base` / `--base-href`, depending on framework).
2. **Asset base** (`<base href>` + bundler `base:`), same source.
3. **Server SPA fallback**: `/web/apps/<id>/*` serves that app's
   `index.html`, so deep links and refresh work.

`HashRouter` is documented as the zero-config fallback (works with no base
at all, ugly URLs). A developer must never hand-write `/web/apps/<id>`
anywhere — if they must know their mount path, the abstraction leaked.

## 4. SDK — `@jellyfin/apps` (decided)

The SDK is a facade over HTTP **and** the single source of runtime context:

- Build-time base (assets) is stamped by the CLI/packager from the manifest
  id. Runtime base (routing, links) is read live — never hardcoded.
- The runtime injects identity into the served `index.html` before the app
  boots (same tag-injection trick plugins already use, but structured):

```html
<script>window.__JELLYFIN_APP__ = {"id":"stats","basePath":"/web/apps/stats","version":"1.2.0"}</script>
```

- SDK surface (sketch):

```js
import { jellyfin } from "@jellyfin/apps";

jellyfin.app.id;         // "stats"
jellyfin.app.basePath;   // "/web/apps/stats" (opaque — join, never parse)

<BrowserRouter basename={jellyfin.app.basePath}>
jellyfin.navigation.to("/users/123");  // joins onto basePath
jellyfin.auth.currentUser();           // incl. roles, for app-internal branching
jellyfin.items.list({ type: "Movie" });
jellyfin.http.get("/Items", {...});    // escape hatch for raw API access
```

- The facade absorbs Jellyfin breaking changes centrally — with two honest
  limits: it only protects calls made through it (raw `fetch` bypasses it),
  and some breaks can only be surfaced, not absorbed. SDK maintenance chases
  every Jellyfin release; that cost is priced in, not avoided.
- Rule to write down early: apps treat `basePath` as opaque. That keeps a
  future move (e.g. per-app domains) from breaking every app in the store.

## 5. Authentication (decided — solved, no asterisks)

Same origin (`/web/apps/*` ⊂ `/web` origin) means the app reads the same
`localStorage` token Jellyfin Web stored. No per-app login pages, passwords,
token storage, discovery, or logout code — ever.

Unauthenticated visit → redirect to `/web/#/login` → return after login.
That is the whole auth design.

## 6. Permissions (decided — coarse, enforceable, honest)

Jellyfin itself is coarse (admin/user, library access), so the platform
mirrors that instead of inventing fine-grained scopes. Split:

**Platform-enforced at serve time (real, shippable in/near MVP):**

- App enabled/disabled globally — kill switch, instant revocation.
- Per-user allow list — composes with Jellyfin's existing access thinking.
- Admin-only flag — for stats/user-management style apps.
- Library scoping rides on the *viewer's* existing library rights, which
  the API already enforces. An app can never show a user what Jellyfin
  itself wouldn't.

**App maker's job:** in-app permission UX (e.g. hiding admin panels from
non-admins via `jellyfin.auth.currentUser()` roles). The platform exposes
the user object and gets out of the way.

**Honesty clause (docs, consent screen):** *"An enabled app acts with the
full API rights of whoever opens it — the same as that user at the API
directly. Install only what you trust."* A true sandbox (scoped per-app
token broker) is explicitly deferred to "only if sandboxing is ever
demanded" — not silently promised via toggles that can't enforce anything.

## 7. App Manifest (framework-neutral)

```json
{
  "id": "stats",
  "name": "Media Statistics",
  "version": "1.2.0",
  "description": "Statistics for your Jellyfin server",
  "runtime": { "entry": "index.html", "spa": true },
  "navigation": { "title": "Statistics", "icon": "analytics" },
  "access": { "enabled": true, "adminOnly": false, "allowedUsers": [] }
}
```

The platform does not care about the app's framework.

## 8. Phased Build (MVP-first)

**MVP (build this first, nothing else):**

1. Plugin registers `/web/apps/*` interception (IStartupFilter middleware —
   proven pattern, File Transformation does exactly this) running BEFORE
   static files.
2. AppRegistry: id → manifest → content root (`<data>/webapps/<id>/dist/`).
   Validate ids, confine paths (no traversal), validate manifests.
3. Static serving + SPA fallback per app.
4. `/web/apps/` launcher listing installed apps.
5. Manifest access block (`enabled`, `adminOnly`, `allowedUsers`) + consent
   screen showing what an app requests.
6. Prove with three apps: React (BrowserRouter + basename), Svelte, vanilla
   (`/web/apps/react-test/`, `/web/apps/svelte-test`,
   `/web/apps/vanilla-test/` incl. deep links like `/foo/bar`).

**After MVP, in order:** SDK (`@jellyfin/apps` + injected identity) →
dev CLI/templates (stamp base paths; HashRouter default) → HMR dev proxy
(`Vite :5173` behind `/web/apps/<id>/*`) → git deployment → build
isolation (sandboxed by default) → repositories → App Store → optional UI
component kit → optional embedded (`<JellyfinApp>`, Shadow DOM
*only here*, never in core) → token broker *if* sandboxing is demanded.

**Explicitly later/never-by-default:** fine-grained scopes, paid/closed
apps, server-side app privileges.

## 9. Security Notes (MVP must-haves vs later)

MVP: id validation, path-traversal confinement, manifest validation,
kill-switch + access gates, uninstall/disable. Later (pre-store design
doc): signing/verification, update rollback, supply-chain stance, CSP
per app, broker design if pursued.

## 10. Suggested Repo Layout (when it grows)

```text
jellyfin-apps/
├── runtime/      # Jellyfin.Plugin.WebApps
├── sdk/          # @jellyfin/apps
├── cli/          # create + dev + build + package
├── templates/    # react / svelte / vue / vanilla
├── registry/     # official-apps.json
└── docs/
```

## 11. First Resident

Port the People browser (`souravrax/Jellyfin_People_Discovery_Plugin`) as
`/web/apps/people/`. It needs almost no changes (single view, no router —
just render under the mount) and proves API auth + serving end to end.
