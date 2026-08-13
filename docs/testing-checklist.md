# Release testing checklist

Real-world conditions to verify before distributing. Marked **[auto]** where a
unit test already covers it, **[manual]** where it needs a real environment.

## Environment

- [ ] **[manual]** Clean Windows account (no dev tools): the published exe runs.
- [ ] **[manual]** Machine **without the .NET runtime**: the self-contained
      build (`publish.ps1`) runs. Verify there is no shared runtime dependency.
- [ ] **[auto]** Paths containing spaces install correctly
      (`Install_WorksWithSpacesInGamePath`).
- [ ] **[manual]** Game folder on a **different drive** than the source folder.
- [ ] **[manual]** A **non-default Steam library**: detect the game via Steam
      and install against a manually entered path on that library.

## Failures and recovery

- [ ] **[auto]** Corrupted archive is refused before anything is written
      (`Install_RejectsArchiveHashMismatch`).
- [ ] **[auto]** Interrupted download/cancel leaves no partial or fake-valid
      file (`Cancellation_RemovesPartial_AndLeavesNoFinalFile`).
- [ ] **[manual]** Interrupt an install half-way (kill the process) and confirm
      the game folder is unchanged and a re-run completes cleanly.
- [ ] **[auto]** Insufficient disk space fails fast without partial files
      (`InsufficientDiskSpace_FailsFast_WithoutDownloading`).
- [ ] **[auto]** Corrupt remote payload is retried then fails cleanly
      (`CorruptSource_FailsCleanly_NoPartialNoFinal`).
- [ ] **[manual]** A stale `.partial` file resumes (or the server re-downloads
      fresh) without producing a corrupt final file.
- [ ] **[manual]** Lock a file in a temporary planning/install workspace and
      confirm cleanup failure does not replace the command's reported result.
- [ ] **[auto]** A second operation for the same game folder is refused while
      the first holds the operation lock, then succeeds after release
      (`GameOperationLockTests`).
- [ ] **[manual]** Start a long install in the desktop app, then try to change
      the same game through the CLI. The CLI should ask you to wait and neither
      operation should leave partial files or journals.

## Mod lifecycle

- [ ] **[manual]** Install a mod, **update** the manifest to a newer archive,
      reinstall the newer version, confirm the newer file replaces the old.
- [ ] **[auto]** An update replaces changed files, adds new files and removes
      obsolete files only while the previous copies still match the journal
      (`Install_UpdateReplacesAddsAndRemovesOwnedFiles`,
      `Install_NewerArchiveUpdatesExistingMod`).
- [ ] **[auto]** An update refuses to overwrite a managed file changed by hand
      (`Install_UpdateRefusesToReplaceModifiedOwnedFile`).
- [ ] **[manual]** **Downgrade** to an older archive and confirm it replaces the
      newer file.
- [ ] **[auto]** Uninstall warns and keeps a file that was modified after
      install (`Uninstall_WarnsButKeepsFile_WhenFileWasModified`).
- [ ] **[auto]** A dependency cycle is reported and blocks the list
      (`DetectsCircularDependencies`).
- [ ] **[auto]** Two mods claiming the same file are refused at pre-flight
      (`SameDestinationAcrossMods_IsReportedOnce`).

## Mod inventory and enable/disable

- [ ] **[auto]** A mod placed in `BepInEx/plugins` by hand (no journal) is listed
      as Unknown (`Discover_HandInstalledMod_IsUnknown`).
- [ ] **[auto]** Disabling moves files out of the game into the manager's disabled folder and enabling moves
      them back (`Disable_MovesFilesToDisabledAndReportsDisabled`,
      `Enable_MovesFilesBackAndReportsInstalled`).
- [ ] **[auto]** A modified file is left in place, not moved, when disabling
      (`Disable_LeavesModifiedFileInPlaceWithWarning`).
- [ ] **[auto]** Uninstall removes a disabled mod from its parked location and
      clears a journal whose files are already gone
      (`Uninstall_DisabledModDeletesParkedFilesAndJournal`,
      `Uninstall_ClearsJournalWhenAllManagedFilesAreAlreadyMissing`).
- [ ] **[auto]** Profile changes save only after file operations succeed and
      leave the previous profile intact on install, dependency, or modified-file
      failures (`ProfileServiceTests`).
- [ ] **[auto]** Concurrent journal, modpack and profile updates retain every
      entry and leave valid JSON; replacement keeps a backup and no temporary
      files (`PersistenceStoreTests`).
- [ ] **[manual]** Disable + enable a mod on the real install and confirm the
      game stops/starts loading it.
- [ ] **[fixed]** A transient test failure turned out to be a real concurrency
      bug: installs shared a temp work-root and deleted it when momentarily
      empty, racing parallel installs. Fixed by never deleting the shared root
      (only per-run subfolders).

## Hosted modpacks (modpacks/)

- [ ] **[manual]** Resize the desktop window at its minimum and normal sizes;
      the navigation remains visible, filters remain usable and cards wrap
      without overlapping or clipping.
- [ ] **[manual]** Search and each Browse filter update the card grid, Reset
      restores the full catalog, and clicking a card opens its details.
- [ ] **[manual]** The NSFW filter remains disabled unless Nexus adds an
      authoritative age-verification field to its OAuth or user response.
- [ ] **[manual]** With a registered Nexus client ID, Settings can sign in,
      display the account name, survive a restart, and sign out cleanly.
- [ ] **[auto]** An expired Nexus session without a refresh token asks the user
      to sign in again without making a token request
      (`RefreshAsync_MissingRefreshToken_AsksForSignInWithoutCallingNexus`).
- [ ] **[auto]** BepInEx is ordered first when a pack includes it
      (`EnforceBepInExFirst_MakesBepInExAResolverDependency`,
      `ModpackInstaller_InstallsBepInExFirstAndRecordsPack`).
- [ ] **[manual]** Install a hosted pack and confirm BepInEx lands first: the
      `BepInEx/` folder exists and the game launches with plugins loaded.
- [ ] **[auto]** The installed pack version is recorded and re-read back
      (`ModpackJournalStore_RecordsAndReadsBack_ReplacingOnRerecord`).
- [ ] **[auto]** A newer published version is flagged, an equal/older one is not
      (`ModpackVersion_IsNewer_Cases`, `UpdateDetection_FlagsNewerPublishedVersion`).
- [ ] **[manual]** Install a pack, then bump `version` in `index.json`; the card
      shows "Update available" and the button reads "Update". Running it should
      not corrupt the existing install.
- [ ] **[manual]** During a large hosted install, move and resize the desktop
      window and confirm it remains responsive until the report appears.
- [ ] **[auto]** `modpack validate` passes a well-formed pack and fails one
      missing the `bepinex` entry, a mod with no source, or a missing logo
      (`ModpackSubmissionTests`).
- [ ] **[manual]** From the repo root, `dotnet run --project
      src/TCGCardShopSimModManager.Cli -- modpack validate` reports
      `[VALID] essential-qol`.
- [ ] **[manual]** Disable + enable a *plugin* mod from an installed pack and
      confirm the game stops/starts loading it. (BepInEx is the framework and is
      intentionally not toggled.)
- [ ] **[note]** The demo pack ships a placeholder BepInEx archive
      (`bepinex-layout.zip`); real packs should point `bepinex`'s `downloadUrl`
      at the official BepInEx release.

## Shipping

- [ ] `dotnet build` — 0 warnings.
- [ ] `dotnet test` — all tests pass.
- [ ] `modpack validate` (no args) reports every pack valid from the repo root.
- [ ] `update-check` reports correctly with no release, with a release, and
      offline.
- [ ] `support-bundle` produces a zip that contains environment info + logs and
      **no** API key.
- [ ] Read `PRIVACY.md`, `THIRD-PARTY-NOTICES.md`; license ship with the exe.
