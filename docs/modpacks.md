# Hosted modpacks

The long-term goal for the desktop app: instead of hand-editing a manifest and
pointing at a folder of archives, you pick a modpack from a list, see what's in
it, and install everything in one click. The packs and their manifests live on
GitHub; the app fetches them, downloads the archives, and runs the same install
pipeline it already uses.

This document is the v1 design. It is the spec to build against.

## Repository layout

Modpacks live in this repo, under `modpacks/`, submitted through pull requests
so they're reviewable like anything else:

```
modpacks/
  index.json              # the gallery index (small, fetched on open + Refresh)
  <packId>/
    manifest.json         # the full mod list (existing ModListManifest shape)
    logo.png              # pack logo, square, ~512x512
```

The index points at each pack's logo and manifest by **repo-relative path**.
The app resolves those to `raw.githubusercontent.com` URLs from one hardcoded
base (the index URL itself), so there's a single place to change if the repo
moves.

## index.json

Kept tiny so the gallery loads instantly — it carries only what the card needs,
not the mod list.

```json
{
  "version": 1,
  "packs": [
    {
      "id": "essential-qol",
      "name": "Essential QoL",
      "shortDescription": "The must-have quality-of-life mods for a first playthrough.",
      "logo": "essential-qol/logo.png",
      "manifest": "essential-qol/manifest.json",
      "version": "1.2.0",
      "updated": "2026-08-12"
    }
  ]
}
```

Fields:

- `id` — stable key; also the folder name under `modpacks/`.
- `name`, `shortDescription` — shown on the card.
- `logo`, `manifest` — repo-relative paths, resolved to raw GitHub URLs.
- `version`, `updated` — shown on the card; `version` is what future update
  detection will compare against the installed journal.

## manifest.json

The existing `ModListManifest` (name, game, mods[]). The one addition for
hosted packs is **per-mod archive sourcing**, because the archives are not in a
single shared folder — they live on Nexus or the mod author's own host.

Each mod resolves its archive in this order:

1. `DownloadUrl` (new optional field) — a direct HTTPS link to the archive.
2. `NexusModId` (+ optional `NexusFileId`) — resolved through the Nexus API
   (already supported by `ModEntry`).
3. Neither present → falls back to a pack-level `source` (an http base URL or a
   local folder), for local-style packs that keep archives together.

`Archive` and `Sha256` stay as they are: after download, the file is hash-checked
against `Sha256` before anything is installed.

The new `DownloadUrl` field is the only schema addition; `NexusModId`/
`NexusFileId` already exist on `ModEntry`.

## Download and install flow

`DeploymentService.Install` reads archives from a **local folder**, matched by
each mod's `Archive` name. The download step is what decides where each archive
comes from. So the app's job is:

1. Fetch `index.json` (on open, and on a Refresh button).
2. For each mod in the chosen pack's manifest, resolve its source and download
   the archive into a cache folder, saved under the mod's `Archive` name.
3. Call the existing `Install(manifest, cacheFolder, gameFolder)`.

Step 2 is the only non-trivial new engine piece: a **per-mod source
dispatcher** — a composite `IModSource` that, for each mod, picks `DownloadUrl`
→ `NexusModId` → pack-level fallback. `ModDownloader`, `HttpModSource` and
`NexusModSource` themselves are reused unchanged, including their caching,
HTTP Range resume, and retry behaviour.

Because `Install` already validates the manifest, plans every archive, and
refuses conflicts before copying a byte, the one-click path is the same safe
pipeline the manual flow uses — just fed from a downloaded cache instead of a
folder the user picked.

## App UI

- A **Modpacks** section with a **grid of cards** (2–3 columns). Each card shows
  the logo, the pack name, and the short description. Scrolls if there are many.
- Clicking a card opens a **detail panel**: the logo, name, short description,
  the full mod list (name + version, read from the manifest), a
  "View manifest on GitHub" link for transparency, and an **Install modpack**
  button.
- **Install modpack** runs the download → install flow above.
- The current manual **manifest + source boxes** stay, below the gallery, as a
  "Local pack" option for people who want to install from files on disk.

## What is reused vs. new

Reused as-is: `ModDownloader`, `HttpModSource`, `NexusModSource`,
`DeploymentService.Install`, the install journal, and Steam auto-detection.

New: the index schema + fetcher, the gallery and detail-panel UI, logo
loading/caching, the per-mod source dispatcher, the selection → download →
install wiring, and the optional `DownloadUrl` field.

## Deferred (not v1)

- **Update detection** — compare a pack's `version` in `index.json` against the
  installed journal and show "Update available". The install pipeline already
  replaces newer/older archives correctly; this is just surfacing it in the UI.
- **Pack-submission validation** — tooling to check a submitted `manifest.json`
  and logo before merge. For v1, review by eye in the PR is enough.
