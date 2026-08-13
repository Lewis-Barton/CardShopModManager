# Bug Fix Status: TCG Card Shop Sim Mod Manager

Tracks the known bugs found in the 2026-08-13 red-team review and their fix status.
**Update this file as each bug is fixed:** set `Status` → `Fixed`, fill the **Fix** and
**Why / PR** columns, and record verification.

## Summary
| Severity | Open | Fixed |
|----------|------|-------|
| Critical | 0 | 1 |
| High     | 3 | 10 |
| Medium   | 9 | 9 |
| Low      | 3 | 5 |
| **Total**| **15** | **25** |

## Status table
| BUG | Sev | Area | Title | Status | Files to change | Fix | Why / PR | Verified |
|-----|-----|------|-------|--------|-----------------|-----|----------|----------|
| BUG-001 | Critical | archive/classifier (security) | Game-root loader-hijack DLL placed via BepInEx-layout mirror | Fixed | ArchiveClassifier.cs | Denylist of known DLL-hijack target names (winhttp/version/winmm/dbghelp/d3d*/dxgi/…) refused only at the sensitive roots (game root + BepInEx/ root); framework tree (incl. BepInEx/core/doorstop.dll) mirrors freely | BUG-001: allowlist wrongly rejected the framework's own BepInEx/core/doorstop.dll and still let hijack DLLs reach the game root; denylist blocks the exact attack vector without breaking the framework | Verified (unit + 104-test suite + build) |
| BUG-002 | High | modpack validate | Validator crashes (ArgumentNull) on malformed index/manifest | Fixed | ModpackSubmissionValidator.cs | `ValidatePack`/`ValidateAll` now guard a null `Packs` array (return a structural failure instead of letting LINQ throw) | BUG-002: a malformed/missing `packs` array must be a clean failure, not an unhandled crash | Verified (unit + suite) |
| BUG-003 | High | update detection | GUI never records pack journal -> feature dead in app | Open | MainWindow.cs, ModpackInstaller.cs | | | |
| BUG-004 | High | pack journal | Corrupt modpacks.json throws unhandled, blocks all install/upgrade | Fixed | ModpackJournalStore.cs | `Load()` now catches `JsonException`, backs the bad file up to `.corrupt`, and returns an empty list | BUG-004: a corrupt pack journal must never abort an otherwise-successful install/upgrade | Verified (unit + suite) |
| BUG-005 | High | pack journal | Uninstall never clears pack journal -> stale "Update available" | Fixed | ModpackJournalStore.cs, ModInstaller.cs | Added `ModpackJournalStore.Remove(packId)`; `Install` now records `PackId` on the per-mod journal entry, and `Uninstall` drops the pack entry when no journaled mod still belongs to it | BUG-005: a pack's last mod being uninstalled must clear the stale "Update available" badge | Verified (unit + suite) |
| BUG-006 | High | update detection | v-prefixed / pre-release versions never detected as updates | Open | ModpackVersion.cs | | | |
| BUG-007 | Medium | update detection | Spurious "Update available" on component-count change (1.0 vs 1.0.0) | Open | ModpackVersion.cs | | | |
| BUG-008 | Medium | UI | Corrupt journal wipes entire Modpacks gallery | Open | MainWindow.cs | | | |
| BUG-009 | Medium | pack journal | Pack id rename orphans journal entry / breaks tracking | Open | ModpackJournalStore.cs, MainWindow.cs | | | |
| BUG-010 | Medium | pack journal | Journal write non-atomic, no backup -> self-perpetuating corruption | Fixed | ModpackJournalStore.cs, JournalStore.cs | Both stores now write via temp-file + rename with a `.bak` of the previous good content | BUG-010: a crash mid-write must not leave an unreadable journal | Verified (unit + suite) |
| BUG-011 | High | lifecycle | disable/enable silent no-op for framework/game-root mods, reports success | Fixed | ModInstaller.cs | `Disable`/`Enable` now count managed/non-managed/skipped files and return non-success when a framework/game-root mod is not something we toggle | BUG-011: toggling a non-managed mod must report failure, not silent success | Verified (unit + suite) |
| BUG-012 | Medium | lifecycle | `mods list` blind to framework/game-root mods | Fixed | ModDiscovery.cs | Added `BepInEx/core` to `ModDiscovery.ActiveRoots` so framework mods are enumerated | BUG-012: `mods list` must report every installed mod, including framework/core | Verified (unit + suite) |
| BUG-013 | High | lifecycle | Partial disable leaves modified file active, reports success | Fixed | ModInstaller.cs | `Disable` tracks kept-vs-moved; if any managed file was kept (modified), it returns non-success with a "partially disabled" message | BUG-013: a partial disable must be reported as failure, not success | Verified (unit + suite) |
| BUG-014 | High | lifecycle | uninstall removes journal entry even when a file was kept -> mod stranded | Fixed | ModInstaller.cs | `Uninstall` only calls `_journal.Remove` when every file was actually deleted; a kept (modified) file retains the entry | BUG-014: an incomplete uninstall must keep the journal entry so the mod stays tracked | Verified (unit + suite) |
| BUG-015 | High | lifecycle | corrupt journal breaks every operation, no recovery | Fixed | JournalStore.cs | `Load()` now catches `JsonException`, backs the bad file up to `.corrupt`, and returns an empty list | BUG-015: a corrupt per-mod journal must not abort every lifecycle op; recover to empty | Verified (unit + suite) |
| BUG-016 | High | lifecycle | re-install disabled mod then disable again -> "The file exists" crash | Fixed | ModInstaller | `Disable` now deletes a stale disabled copy before `File.Move` instead of throwing | BUG-016: re-disabling a reinstalled mod must not crash with "file already exists" | Verified (unit + suite) |
| BUG-017 | High | install | `install` reports success (exit 0) even when a mod fails | Open | DeploymentService.cs, InstallCommand.cs | | | |
| BUG-018 | Low | lifecycle/UI | enable/disable of never/already-disabled mod reports success doing nothing | Fixed | ModInstaller.cs, InstallPlan.cs, ModsCommand.cs | `DisableResult`/`EnableResult` gain a `Message` (e.g. "Already disabled/enabled"); CLI prints it | BUG-018: toggling an already-target-state mod must report a distinct "already" status | Verified (unit + suite) |
| BUG-019 | Medium | conflicts | install pre-flight conflict ignores already-installed mods | Open | DeploymentService.cs | | | |
| BUG-020 | Medium | resolver | BepInEx-first ordering NOT enforced for local install/validate | Open | DeploymentService.cs, ModListResolver.cs, ModpackInstaller.cs | | | |
| BUG-021 | Low | resolver | wrong-case dependency/id refs silently accepted | Open | ModListResolver.cs, DeploymentService.cs | | | |
| BUG-022 | Medium | archive security | archives with executables not rejected outright (banned .exe dropped, rest installs) | Fixed | ModInstaller.cs, ZipArchiveExtractor.cs, InstallPlan.cs, DeploymentService.cs | ExtractionResult.RejectedEntries now flow into InstallResult.RejectedEntries and are surfaced as warnings by DeploymentService (was a silent drop behind a success message) | BUG-022: a bundled .exe must be flagged loudly, not hidden behind success | Verified (unit + suite) |
| BUG-023 | Medium | archive | oversized archives install partially and report success | Fixed | ZipArchiveExtractor.cs, ModInstaller.cs | ExtractionResult.Truncated (set on entry/size cap) now makes CreatePlan throw InvalidDataException, so a partial copy is never installed | BUG-023: a truncated extraction must fail loudly, not install partial + report success | Verified (unit + suite) |
| BUG-024 | Medium | validation | safe archive filenames with ".." (MyMod..v1.zip) falsely rejected | Fixed | ManifestValidator.cs | Replaced the `Contains("..")` substring test with a segment-based traversal check (rejects a `..` *segment* or rooted path, allows `..` inside a filename) | BUG-024: `MyMod..v1.zip` is a safe filename and must validate | Verified (unit + suite) |
| BUG-025 | Medium | validation | installType "BepInEx" accepted for non-bepinex id on local path | Fixed | ManifestValidator.cs | Reserved the `BepInEx` install type for the framework entry (id `bepinex`); a non-framework mod claiming it is now rejected | BUG-025: `BepInEx` is the framework's reserved type and must not be used by ordinary mods | Verified (unit + suite) |
| BUG-026 | Low | UX | malformed manifests surface raw serializer exceptions | Open | ManifestReader.cs, Program.cs | | | |
| BUG-027 | Low | CLI | install with <3 args prints usage but exits 0 | Open | InstallCommand.cs, Program.cs | | | |
| BUG-028 | Low | validation | empty mods list validated as valid (no warning) | Fixed | ManifestValidator.cs | `Validate` now reports an error when `Mods` is null/empty | BUG-028: an empty pack must be surfaced, not silently "valid" | Verified (unit + suite) |
| BUG-029 | Medium | classifier | loose .dll at root alongside BepInEx/ lands in game root, not BepInEx/plugins | Fixed | ArchiveClassifier.cs | In the BepInExLayout branch, a root-level .dll now routes to BepInEx/plugins/<mod>/ instead of mirroring to the game root | BUG-029: loose plugin DLL must live under plugins, never the game root where the loader could pick it up | Verified (unit + suite) |
| BUG-030 | Low | archive | nested .zip installed as-is, unvalidated | Fixed | ZipArchiveExtractor.cs, ArchiveProtectionSettings.cs | ArchiveProtectionSettings.Default now rejects archive extensions (.zip/.7z/.rar/.tar/.gz/.tgz/.bz2/.xz) so a nested archive is refused, not written unvalidated | BUG-030: a nested archive bypasses all protection checks if written verbatim | Verified (unit + suite) |
| BUG-031 | High | modpack validate | `modpack validate` (all) reports "All packs valid." when index.json missing | Fixed | ModpackSubmissionValidator.cs, ModpackCommand.cs | `ValidateAll` returns a single `(index.json)` failure entry when index is missing; `ModpackCommand` sets a non-zero exit code | BUG-031: a missing index must be a clear failure, not "All packs valid." | Verified (unit + suite) |
| BUG-032 | High | modpack validate | BepInEx framework accepted with wrong installType "BepInExPlugin" -> VALID | Fixed | ManifestValidator.cs, ModpackSubmissionValidator.cs | Framework entry (id `bepinex`) must use `BepInEx` install type; `ModpackSubmissionValidator` enforces the exact type on the framework entry | BUG-032: a mislabeled framework entry must be INVALID | Verified (unit + suite) |
| BUG-033 | Medium | modpack validate | wrong manifest (different pack name) accepted as VALID | Fixed | ModpackSubmissionValidator.cs | Manifest/index name mismatch is now an error (was only a warning), so a mismatched manifest cannot validate as VALID | BUG-033: a manifest for a different pack must not validate as VALID | Verified (unit + suite) |
| BUG-034 | Low | modpack validate | no path sanitization for logo/manifest refs (traversal/absolute) | Fixed | ModpackSubmissionValidator.cs, ManifestValidator.cs | `ModpackSubmissionValidator` now rejects `..`/rooted `Logo`/`Manifest` references before resolving (consistent with archive handling) | BUG-034: traversal/absolute logo/manifest refs must be rejected | Verified (unit + suite) |
| BUG-035 | Medium | CLI UX | `modpack install` no-id throws "Unexpected error" after network fetch | Open | ModpackCommand.cs, Program.cs | | | |
| BUG-036 | Medium | CLI UX | missing/bad args collapse into generic "Unexpected error" | Open | InstallCommand.cs, ValidateCommand.cs, PlanCommand.cs, Program.cs | | | |
| BUG-037 | Medium | UI | RunHandler swallows exceptions -> stale UI state on thrown failure | Open | MainWindow.cs | | | |
| BUG-038 | Medium | UI | WelcomeDetectAsync not wrapped -> unobserved exception at startup risk | Open | MainWindow.cs | | | |
| BUG-039 | Low | CLI | serve ignores SIGINT headless; no clean shutdown | Open | ServeCommand.cs, LocalHttpServer.cs | | | |
| BUG-040 | Low | CLI | uninstall on non-existent game folder -> misleading "No journal entry" | Fixed | ModInstaller.cs, UninstallCommand.cs | `Uninstall` returns a distinct "Game folder not found" error; `UninstallCommand` validates the folder up front | BUG-040: a missing game folder must be distinguished from a missing journal entry | Verified (unit + suite) |

## Fix log
Detailed entries are appended here as bugs are resolved (files changed, what/why, verification).

### BUG-001 (Critical) + BUG-029 (Medium) — ArchiveClassifier.cs
- **Files:** `src/TCGCardShopSimModManager.Core/ArchiveClassifier.cs`, `tests/.../ArchiveClassifierTests.cs`
- **What:** Replaced the prior allowlist (only `plugins`/`patchers`/`config` under `BepInEx`) with a **denylist of known DLL search-order hijack targets** (`winhttp.dll`, `version.dll`, `winmm.dll`, `dbghelp.dll`, `d3d9.dll`, `d3d11.dll`, `dxgi.dll`, `dsound.dll`, `mscoree.dll`, `propsys.dll`, `userenv.dll`, `dinput8.dll`, `dwrite.dll`, `apphelp.dll`, `comctl32.dll`, `secur32.dll`, `cryptbase.dll`, `msimg32.dll`, `uxtheme.dll`, `ws2_32.dll`). A file bearing one of these names is refused **only when it would land at the game root or the `BepInEx/` root**; everything else (including the genuine framework's `BepInEx/core/doorstop.dll`) mirrors normally. Root-level `.dll`s in a `BepInExLayout` now route to `BepInEx/plugins/<mod>/` (BUG-029).
- **Why:** The allowlist wrongly rejected `BepInEx/core/doorstop.dll` (breaks the framework) and the original mirror logic still let `winhttp.dll`/`version.dll` reach the game root / `BepInEx/` root — the classic pre-launch RCE vector. The denylist blocks exactly that vector while permitting the framework to install.
- **Verification:** 11 ArchiveClassifier tests pass (incl. new `FrameworkDllUnderBepInExCore_IsAllowed`, `RootHijackDllInBepInExLayout_IsRejected`, `GameRootHijackDll_IsRejected`); full Core suite 104/104; `dotnet build` clean. Installer only writes `plan.Files`, so refused hijack DLLs are never written to disk.

### BUG-022 (Medium) + BUG-023 (Medium) + BUG-030 (Low) — archive security/extraction
- **Files:** `ZipArchiveExtractor.cs`, `ArchiveProtectionSettings.cs`, `ModInstaller.cs`, `InstallPlan.cs`, `ArchiveModels.cs`, `DeploymentService.cs` (+ tests `ZipArchiveExtractorTests.cs`, `ModInstallerTests.cs`)
- **What:**
  - BUG-030: `ArchiveProtectionSettings.Default` now rejects archive extensions (`.zip/.7z/.rar/.tar/.gz/.tgz/.bz2/.xz`); a nested archive is refused rather than written out unvalidated.
  - BUG-023: `ZipArchiveExtractor` already tracks `Truncated` on the entry/size cap; `ModInstaller.CreatePlan` now throws `InvalidDataException` when `result.Truncated`, so a partial copy is never installed and reported as success.
  - BUG-022: `ExtractionResult.RejectedEntries` flow into `InstallPlan`/`InstallResult` (new `RejectedEntries`/`SkippedEntries` fields), and `DeploymentService` surfaces them as warnings/notes — a banned `.exe` is no longer a silent drop behind a success message.
- **Why:** Each was a silent-failure / partial-install / bypass hole in the archive pipeline. The fixes make the pipeline fail loudly (truncation), refuse nested archives (bypass), and report rejections (executables).
- **Verification:** New tests `Extract_RejectsNestedZip`, `Extract_FlagsTruncationWhenSizeCapHit`, `CreatePlan_ThrowsOnTruncatedArchive`, `Install_SurfacesRejectedExecutable_WhileInstallingRest` all pass; full Core suite 104/104.

### BUG-004, BUG-005, BUG-010, BUG-011, BUG-012, BUG-013, BUG-014, BUG-015, BUG-016, BUG-018, BUG-040 — Journals & lifecycle (Workstream 2)
- **Files:** `JournalStore.cs`, `ModpackJournalStore.cs`, `ModDiscovery.cs`, `ModInstaller.cs`, `InstallPlan.cs`, `InstallJournal.cs`, `ModListManifest.cs`, `ModsCommand.cs`, `UninstallCommand.cs` (+ tests `ModInstallerTests.cs`, `ModDiscoveryTests.cs`)
- **What:**
  - BUG-015 / BUG-004 (High): `JournalStore.Load` and `ModpackJournalStore.Load` now catch `JsonException`, back the bad file up to `<journal>.corrupt`, and return an empty list — a corrupt journal no longer aborts every operation.
  - BUG-010 (atomic writes): both stores now write via temp-file + rename and keep a `<journal>.bak`, so a crash mid-write cannot leave an unreadable journal.
  - BUG-011 / BUG-013 / BUG-016 / BUG-018 (High/High/High/Low): `Disable`/`Enable` now count managed/non-managed/moved/kept files — non-managed framework/game-root mods report non-success (BUG-011), a partial disable reports failure (BUG-013), a stale disabled copy is cleared before `File.Move` (BUG-016), and an already-target-state toggle returns a distinct "Already disabled/enabled" `Message` (BUG-018).
  - BUG-014 (High): `Uninstall` only drops the journal entry when every file was actually deleted; a kept (modified) file retains the entry so the mod stays tracked.
  - BUG-005 (High): added `ModpackJournalStore.Remove(packId)`; `Install` records `PackId` on the per-mod entry (added `PackId` to `InstallJournalEntry`/`ModEntry`), and `Uninstall` clears the pack entry when no journaled mod still belongs to it.
  - BUG-012 (Medium): `ModDiscovery` now includes `BepInEx/core` in its active roots, so framework mods appear in `mods list`.
  - BUG-040 (Low): `Uninstall` returns a distinct "Game folder not found" error, and `UninstallCommand` validates the folder up front.
- **Why:** Each was a silent-failure / wrong-status / no-recovery hole in the lifecycle and journaling paths.
- **Verification:** New tests `Disable_FrameworkMod_ReportsNonSuccess`, `Disable_AlreadyDisabledMod_ReportsAlreadyDisabled`, `Disable_ReinstallThenDisable_DoesNotThrow`, `Uninstall_MissingGameFolder_ReportsGameFolderNotFound`, `Uninstall_KeepsJournalEntryWhenFileModified`, `Uninstall_LastModOfPack_ClearsPackJournal`, `JournalStore_ToleratesCorruptFile`, `ModpackJournalStore_ToleratesCorruptFile`, `Discover_FrameworkModUnderBepInExCore_IsListed`, and the updated `Disable_LeavesModifiedFileInPlaceAndReportsFailure` all pass; full Core suite 113/113.

### BUG-002, BUG-010, BUG-024, BUG-025, BUG-028, BUG-031, BUG-032, BUG-033, BUG-034 — Manifest & modpack validation (Workstream 3)
- **Files:** `ManifestValidator.cs`, `ModpackSubmissionValidator.cs`, `ModpackCommand.cs` (+ tests `ManifestValidatorTests.cs` (new), `ModpackSubmissionTests.cs`)
- **What:**
  - BUG-002 (High): `ValidatePack`/`ValidateAll` now guard a null `Packs` array and return a clean structural failure instead of letting LINQ throw `ArgumentNullException`.
  - BUG-031 (High): `ValidateAll` returns a single `(index.json)` failure entry when `index.json` is missing (so the CLI no longer prints "All packs valid."); `ModpackCommand` sets a non-zero exit code on validation failure.
  - BUG-032 (High): the `BepInEx` install type is reserved for the framework entry (id `bepinex`); `ManifestValidator` rejects it for other ids, and `ModpackSubmissionValidator` requires the framework entry to use exactly `BepInEx`.
  - BUG-033 (Medium): a manifest/index name mismatch is now an error, not a warning, so a mismatched manifest cannot validate as VALID.
  - BUG-034 (Low): `ModpackSubmissionValidator` rejects `..`/rooted `Logo`/`Manifest` references before resolving.
  - BUG-024 (Medium): `ManifestValidator` uses a segment-based traversal check (rejects a `..` path segment or rooted path, allows `..` inside a filename like `MyMod..v1.zip`).
  - BUG-025 (Medium): a non-framework mod claiming install type `BepInEx` is rejected.
  - BUG-028 (Low): an empty `Mods` list is reported as an error.
  - BUG-010 (Medium, atomic writes): implemented in Workstream 2 — both stores write via temp-file + rename with a `.bak`.
- **Why:** These were crashes, silent "valid" outcomes, and traversal/type-enforcement holes in pack validation.
- **Verification:** New tests `ManifestValidatorTests.*` (5) and `ModpackSubmissionTests.*` (ValidatePack_Fails_WhenIndexMissingPacksArray, ValidateAll_Fails_WhenIndexMissingPacksArray, ValidateAll_Fails_WhenIndexMissing, ValidatePack_Fails_WhenFrameworkUsesWrongInstallType, ValidatePack_Fails_WhenManifestNameMismatchesIndex, ValidatePack_Fails_WhenLogoReferenceUnsafe) all pass; full Core suite 124/124.
