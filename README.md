# JellyfinApps

Turn Jellyfin into a platform for independently developed web applications.

A Jellyfin App is a normal web application — React, Svelte, Vue, Solid,
vanilla JS — hosted by the Jellyfin server at:

```text
http://jellyfin:8096/web/apps/<app-id>/
```

See [PLAN.md](PLAN.md) for the full design brief (vision, architecture,
routing, SDK, auth, permissions, phased build).

## Layout

```text
jellyfin-apps/
├── runtime/      # Jellyfin.Plugin.WebApps (Phase 1: MVP core)
├── sdk/          # @jellyfin/apps (after MVP)
├── cli/          # create + dev + build + package (after MVP)
├── templates/    # react / svelte / vue / vanilla (after MVP)
├── registry/     # official-apps.json (later)
└── docs/
```

## Requirements

- Jellyfin **12+** (server), .NET **10** SDK to build.

## Phase 1 — Runtime MVP core (done)

Plugin registers `/web/apps/*` interception running BEFORE static files,
AppRegistry (`id → manifest → content root`), static serving + SPA fallback,
launcher at `/web/apps/`.

## Build

```powershell
dotnet build runtime/Jellyfin.Plugin.WebApps.slnx
dotnet test runtime/Jellyfin.Plugin.WebApps.slnx
```

## Try it (manual)

1. Copy `runtime/_dev/webapps/vanilla-test/` to
   `&lt;jellyfin-data&gt;/webapps/vanilla-test/`.
2. Drop the built `Jellyfin.Plugin.WebApps.dll` into the server plugins folder.
3. Restart Jellyfin 12, open `/web/apps/` → `vanilla-test`,
   incl. deep link `/web/apps/vanilla-test/foo/bar`.
