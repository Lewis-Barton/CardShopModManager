# Bundled mod packs (planned)

Idea for later, not implemented yet: ship well-known, pre-configured mod lists
with the program so end users can install a whole pack without writing a
manifest.

Planned shape:

- A `modpacks/` folder (embedded or alongside the app) containing ready-made
  manifests — same manifest format as today, so the whole engine works unchanged.
- Each pack has an id, a display name, a description and its own manifest.
- UX: the UI lists available packs; picking one fills the manifest path and the
  source folder (for Nexus-hosted packs, just a manifest + `download nexus`).
- Packs must follow the same rules as any list: hashes required, no scripts,
  layout rules apply. Nothing about the installer changes — a pack is just a
  manifest you didn't have to author.

Constraints that keep it safe:

- Packs are versioned with the manifest `manifestVersion`; unknown versions are
  refused rather than guessed at.
- A pack never pins a game path — detection is the user's Steam install.
- Pack archives come from the same trusted sources as today (local folder,
  HTTPS, Nexus).

This is a "later" item — the roadmap item it belongs to is the built-in modlist
editor / discovery feature, which stays out of the first public version.