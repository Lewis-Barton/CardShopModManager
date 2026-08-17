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

A `essential-qol` demo pack ships in this repo so the gallery has something to
show before real packs exist. Its manifest points `downloadUrl` at a sample
archive already committed under `samples/mod-archives/`, so the whole flow —
fetch index, fetch manifest, download, install — works once `modpacks/` is
pushed. Replace it (or add your own) with real packs when you have archives
hosted on Nexus or an author's URL.

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
      "updated": "2026-08-12",
      "featured": true,
      "nsfw": false,
      "downloadSize": 285000000,
      "tags": ["quality-of-life", "starter"],
      "modIds": ["bepinex", "example-mod"]
    }
  ]
}
```

Fields:

- `id` — stable key; also the folder name under `modpacks/`.
- `name`, `shortDescription` — shown on the card.
- `logo`, `manifest` — repo-relative paths, resolved to raw GitHub URLs.
- `version`, `updated` — shown on the card; `version` is compared against the
  installed pack journal (`cardshopmodmanager.modpacks.json` in the game folder)
  so the app can show "Update available" when a newer pack is published.
- `featured`, `nsfw`, `downloadSize`, `tags`, `modIds` — optional gallery
  metadata used by the desktop filters. `downloadSize` is the total compressed
  download size in bytes. Older index entries can omit these fields.

NSFW packs stay hidden. Nexus requires age verification for restricted content,
but its documented OAuth token and user-validation response do not expose that
result to third-party clients. Do not enable the filter based on login alone.

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

**Disk-space pre-flight:** a pack may declare a top-level `totalSize` (bytes, the
sum of its mod archives). When present, the installer checks free space on both
the download temp location and the game folder *before* fetching anything, and
fails fast with a clear message if either is short — so a large pack won't
partially download and then stall on a full disk. The per-file gate in
`ModDownloader` remains as a backstop for any mod whose real size exceeds the
declared total.

The new `DownloadUrl` field and the optional top-level `totalSize` are the schema
additions; `NexusModId`/`NexusFileId` already exist on `ModEntry`.

Valid `installType` values are `BepInExPlugin` (a plugin that loads inside
BepInEx) and `BepInEx` (the BepInEx framework itself — see below). The
on-disk layout of every entry is decided by `ArchiveClassifier` from the
archive's contents, not by `installType`.

### BepInEx must come first

Every modpack must include the **BepInEx framework** as a mod entry, with the
reserved `id` `bepinex` and `installType` `BepInEx`:

```json
{
  "id": "bepinex",
  "name": "BepInEx",
  "version": "5.4.23",
  "archive": "bepinex.zip",
  "sha256": "<sha256 of the BepInEx archive>",
  "installType": "BepInEx",
  "dependencies": [],
  "conflicts": []
}
```

BepInEx is the loader every plugin runs inside, so it has to be on disk before
any plugin is copied in. `ModpackInstaller.EnforceBepInExFirst` guarantees this:
at install time it makes **every other mod depend on `bepinex`** (if it doesn't
already), and the resolver orders dependencies first — so pack authors can't
forget it. The current classifier installs a top-level `BepInEx/` folder but
rejects root-level loader DLLs such as `winhttp.dll`. Real framework packages
that rely on that bootstrap file are not yet supported end to end.

The demo pack points `bepinex`'s `downloadUrl` at the committed
`samples/mod-archives/bepinex-layout.zip` placeholder so the flow is
self-contained and testable; a real pack should point at the official BepInEx
release archive instead.

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

- **Browse modpacks** shows a wrapping grid of cards beside fixed search, tag,
  featured and NSFW filters. Each card shows the logo, name, short description,
  tags and compressed download size when available.
- Clicking a card opens a modal with the logo, description and full mod list.
  The modal owns the **Install modpack** or **Update** action.
- A newer published version adds an **Update available** badge to its card.
- **Manage mods** contains game-folder selection and installed-mod lifecycle
  controls. Local manifest workflows remain available through the CLI.
- **Settings** contains Nexus sign-in, update checks and support-bundle export.

## Validating a submission

Before merging a pack, run the local check from the repo root:

```
dotnet run --project src/TCGCardShopSimModManager.Cli -- modpack validate [packId]
```

With no `packId` it checks every pack listed in `modpacks/index.json`. It reads
`index.json`, the referenced `manifest.json` and `logo` from disk — it never
contacts GitHub — and fails the submission on:

- a missing or non-JSON `index.json` / `manifest.json`;
- a missing `logo`, or one that isn't a PNG;
- a manifest that fails `ManifestValidator`, including a name that does not
  match the index entry;
- a mod with no resolvable source (`DownloadUrl`, `NexusModId`, or a pack-level
  `source`);
- a pack that omits the required `bepinex` entry (see BepInEx above).

It warns (without failing) on a suspiciously small logo.

## What is reused vs. new

Reused as-is: `ModDownloader`, `HttpModSource`, `NexusModSource`,
`DeploymentService.Install`, the install journal, and Steam auto-detection.

New: the index schema + fetcher, the gallery and detail-panel UI, logo
loading/caching, the per-mod source dispatcher, the selection → download →
install wiring, and the optional `DownloadUrl` field.

## Deferred (not v1)

- (Pack-submission validation is done — see "Validating a submission" above.)
