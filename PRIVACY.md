# Privacy

Card Shop Mod Manager collects nothing and sends nothing on its own.

- **No telemetry.** The app makes no network calls on its own. The only network calls are ones
  you start: downloading mods (`download`), and `update-check`, which asks GitHub
  for the latest release tag when you run it.
- **Crash data stays local.** An unexpected error is written to a diagnostic log
  on this machine only. Nothing is uploaded, and there is no opt-in anywhere
  that would send it.
- **Your Nexus key stays on this machine.** `nexus set-key` stores the key
  encrypted with DPAPI, readable only by the current Windows user, in
  `%LOCALAPPDATA%\TCGCardShopSimModManager`. It is never written into the project, the
  logs, or the support bundle.
- **Diagnostic logs** are plain text in `%LOCALAPPDATA%\TCGCardShopSimModManager\logs`
  (override with the `CSMM_LOG_DIR` environment variable). You can delete them
  at any time. The `support-bundle` command collects them into a zip you share
  only if you choose to.
- **The support bundle** contains environment info and recent log lines. It
  deliberately excludes anything that could be a key or credential.
- **Installed-mod records** (`cardshopmodmanager.journal.json`,
  `cardshopmodmanager.profiles.json`) live inside the game folder you manage.
  They record file paths and hashes only.

This project is open source: the source of truth for the privacy behaviour is
the code, which anyone can inspect.