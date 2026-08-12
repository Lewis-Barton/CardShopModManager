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

## Mod lifecycle

- [ ] **[manual]** Install a mod, **update** the manifest to a newer archive,
      reinstall the newer version, confirm the newer file replaces the old.
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
      as Unknown, not hidden (`Discover_HandInstalledMod_IsUnknown`).
- [ ] **[auto]** Disabling moves files to `BepInEx/disabled` and enabling moves
      them back (`Disable_MovesFilesToDisabledAndReportsDisabled`,
      `Enable_MovesFilesBackAndReportsInstalled`).
- [ ] **[auto]** A modified file is left in place, not moved, when disabling
      (`Disable_LeavesModifiedFileInPlaceWithWarning`).
- [ ] **[manual]** Disable + enable a mod on the real install and confirm the
      game stops/starts loading it.
- [ ] **[fixed]** A transient test failure turned out to be a real concurrency
      bug: installs shared a temp work-root and deleted it when momentarily
      empty, racing parallel installs. Fixed by never deleting the shared root
      (only per-run subfolders).

## Shipping

- [ ] `dotnet build` — 0 warnings.
- [ ] `dotnet test` — all tests pass.
- [ ] `update-check` reports correctly with no release, with a release, and
      offline.
- [ ] `support-bundle` produces a zip that contains environment info + logs and
      **no** API key.
- [ ] Read `PRIVACY.md`, `THIRD-PARTY-NOTICES.md`; license ship with the exe.