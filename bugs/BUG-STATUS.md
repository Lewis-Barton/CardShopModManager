# Bug Fix Status: TCG Card Shop Sim Mod Manager

Tracks the known bugs found in the 2026-08-13 red-team review and their fix status.
**Update this file as each bug is fixed:** set `Status` → `Fixed`, fill the **Fix** and
**Why / PR** columns, and record verification.

## Summary
| Severity | Open | Fixed |
|----------|------|-------|
| Critical | 0 | 1 |
| High     | 13 | 0 |
| Medium   | 17 | 1 |
| Low      | 8 | 0 |
| **Total**| **38** | **2** |

## Status table
| BUG | Sev | Area | Title | Status | Files to change | Fix | Why / PR | Verified |
|-----|-----|------|-------|--------|-----------------|-----|----------|----------|
| BUG-001 | Critical | archive/classifier (security) | Game-root loader-hijack DLL placed via BepInEx-layout mirror | Fixed | ArchiveClassifier.cs | Denylist of known DLL-hijack target names (winhttp/version/winmm/dbghelp/d3d*/dxgi/…) refused only at the sensitive roots (game root + BepInEx/ root); framework tree (incl. BepInEx/core/doorstop.dll) mirrors freely | BUG-001: allowlist wrongly rejected the framework's own BepInEx/core/doorstop.dll and still let hijack DLLs reach the game root; denylist blocks the exact attack vector without breaking the framework | Verified (unit + 104-test suite + build) |
| BUG-002 | High | modpack validate | Validator crashes (ArgumentNull) on malformed index/manifest | Open | ModpackSubmissionValidator.cs | | | |
| BUG-003 | High | update detection | GUI never records pack journal -> feature dead in app | Open | MainWindow.cs, ModpackInstaller.cs | | | |
| BUG-004 | High | pack journal | Corrupt modpacks.json throws unhandled, blocks all install/upgrade | Open | ModpackJournalStore.cs | | | |
| BUG-005 | High | pack journal | Uninstall never clears pack journal -> stale "Update available" | Open | ModpackJournalStore.cs, ModInstaller.cs | | | |
| BUG-006 | High | update detection | v-prefixed / pre-release versions never detected as updates | Open | ModpackVersion.cs | | | |
| BUG-007 | Medium | update detection | Spurious "Update available" on component-count change (1.0 vs 1.0.0) | Open | ModpackVersion.cs | | | |
| BUG-008 | Medium | UI | Corrupt journal wipes entire Modpacks gallery | Open | MainWindow.cs | | | |
| BUG-009 | Medium | pack journal | Pack id rename orphans journal entry / breaks tracking | Open | ModpackJournalStore.cs, MainWindow.cs | | | |
| BUG-010 | Medium | pack journal | Journal write non-atomic, no backup -> self-perpetuating corruption | Open | ModpackJournalStore.cs, JournalStore.cs | | | |
| BUG-011 | High | lifecycle | disable/enable silent no-op for framework/game-root mods, reports success | Open | ModInstaller.cs | | | |
| BUG-012 | Medium | lifecycle | `mods list` blind to framework/game-root mods | Open | ModDiscovery.cs | | | |
| BUG-013 | High | lifecycle | Partial disable leaves modified file active, reports success | Open | ModInstaller.cs | | | |
| BUG-014 | High | lifecycle | uninstall removes journal entry even when a file was kept -> mod stranded | Open | ModInstaller.cs | | | |
| BUG-015 | High | lifecycle | corrupt journal breaks every operation, no recovery | Open | JournalStore.cs | | | |
| BUG-016 | High | lifecycle | re-install disabled mod then disable again -> "The file exists" crash | Open | ModInstaller.cs | | | |
| BUG-017 | High | install | `install` reports success (exit 0) even when a mod fails | Open | DeploymentService.cs, InstallCommand.cs | | | |
| BUG-018 | Low | lifecycle/UI | enable/disable of never/already-disabled mod reports success doing nothing | Open | ModInstaller.cs | | | |
| BUG-019 | Medium | conflicts | install pre-flight conflict ignores already-installed mods | Open | DeploymentService.cs | | | |
| BUG-020 | Medium | resolver | BepInEx-first ordering NOT enforced for local install/validate | Open | DeploymentService.cs, ModListResolver.cs, ModpackInstaller.cs | | | |
| BUG-021 | Low | resolver | wrong-case dependency/id refs silently accepted | Open | ModListResolver.cs, DeploymentService.cs | | | |
| BUG-022 | Medium | archive security | archives with executables not rejected outright (banned .exe dropped, rest installs) | Open | ModInstaller.cs, ZipArchiveExtractor.cs | | | |
| BUG-023 | Medium | archive | oversized archives install partially and report success | Open | ZipArchiveExtractor.cs, ModInstaller.cs | | | |
| BUG-024 | Medium | validation | safe archive filenames with ".." (MyMod..v1.zip) falsely rejected | Open | ManifestValidator.cs | | | |
| BUG-025 | Medium | validation | installType "BepInEx" accepted for non-bepinex id on local path | Open | ManifestValidator.cs | | | |
| BUG-026 | Low | UX | malformed manifests surface raw serializer exceptions | Open | ManifestReader.cs, Program.cs | | | |
| BUG-027 | Low | CLI | install with <3 args prints usage but exits 0 | Open | InstallCommand.cs, Program.cs | | | |
| BUG-028 | Low | validation | empty mods list validated as valid (no warning) | Open | ManifestValidator.cs | | | |
| BUG-029 | Medium | classifier | loose .dll at root alongside BepInEx/ lands in game root, not BepInEx/plugins | Fixed | ArchiveClassifier.cs | In the BepInExLayout branch, a root-level .dll now routes to BepInEx/plugins/<mod>/ instead of mirroring to the game root | BUG-029: loose plugin DLL must live under plugins, never the game root where the loader could pick it up | Verified (unit + suite) |
| BUG-030 | Low | archive | nested .zip installed as-is, unvalidated | Open | ZipArchiveExtractor.cs, ArchiveProtectionSettings.cs | | | |
| BUG-031 | High | modpack validate | `modpack validate` (all) reports "All packs valid." when index.json missing | Open | ModpackSubmissionValidator.cs, ModpackCommand.cs | | | |
| BUG-032 | High | modpack validate | BepInEx framework accepted with wrong installType "BepInExPlugin" -> VALID | Open | ManifestValidator.cs, ModpackSubmissionValidator.cs | | | |
| BUG-033 | Medium | modpack validate | wrong manifest (different pack name) accepted as VALID | Open | ModpackSubmissionValidator.cs | | | |
| BUG-034 | Low | modpack validate | no path sanitization for logo/manifest refs (traversal/absolute) | Open | ModpackSubmissionValidator.cs, ManifestValidator.cs | | | |
| BUG-035 | Medium | CLI UX | `modpack install` no-id throws "Unexpected error" after network fetch | Open | ModpackCommand.cs, Program.cs | | | |
| BUG-036 | Medium | CLI UX | missing/bad args collapse into generic "Unexpected error" | Open | InstallCommand.cs, ValidateCommand.cs, PlanCommand.cs, Program.cs | | | |
| BUG-037 | Medium | UI | RunHandler swallows exceptions -> stale UI state on thrown failure | Open | MainWindow.cs | | | |
| BUG-038 | Medium | UI | WelcomeDetectAsync not wrapped -> unobserved exception at startup risk | Open | MainWindow.cs | | | |
| BUG-039 | Low | CLI | serve ignores SIGINT headless; no clean shutdown | Open | ServeCommand.cs, LocalHttpServer.cs | | | |
| BUG-040 | Low | CLI | uninstall on non-existent game folder -> misleading "No journal entry" | Open | ModInstaller.cs, UninstallCommand.cs | | | |

## Fix log
Detailed entries are appended here as bugs are resolved (files changed, what/why, verification).

### BUG-001 (Critical) + BUG-029 (Medium) — ArchiveClassifier.cs
- **Files:** `src/TCGCardShopSimModManager.Core/ArchiveClassifier.cs`, `tests/.../ArchiveClassifierTests.cs`
- **What:** Replaced the prior allowlist (only `plugins`/`patchers`/`config` under `BepInEx`) with a **denylist of known DLL search-order hijack targets** (`winhttp.dll`, `version.dll`, `winmm.dll`, `dbghelp.dll`, `d3d9.dll`, `d3d11.dll`, `dxgi.dll`, `dsound.dll`, `mscoree.dll`, `propsys.dll`, `userenv.dll`, `dinput8.dll`, `dwrite.dll`, `apphelp.dll`, `comctl32.dll`, `secur32.dll`, `cryptbase.dll`, `msimg32.dll`, `uxtheme.dll`, `ws2_32.dll`). A file bearing one of these names is refused **only when it would land at the game root or the `BepInEx/` root**; everything else (including the genuine framework's `BepInEx/core/doorstop.dll`) mirrors normally. Root-level `.dll`s in a `BepInExLayout` now route to `BepInEx/plugins/<mod>/` (BUG-029).
- **Why:** The allowlist wrongly rejected `BepInEx/core/doorstop.dll` (breaks the framework) and the original mirror logic still let `winhttp.dll`/`version.dll` reach the game root / `BepInEx/` root — the classic pre-launch RCE vector. The denylist blocks exactly that vector while permitting the framework to install.
- **Verification:** 11 ArchiveClassifier tests pass (incl. new `FrameworkDllUnderBepInExCore_IsAllowed`, `RootHijackDllInBepInExLayout_IsRejected`, `GameRootHijackDll_IsRejected`); full Core suite 104/104; `dotnet build` clean. Installer only writes `plan.Files`, so refused hijack DLLs are never written to disk.
