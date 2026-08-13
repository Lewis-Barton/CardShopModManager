using System;
using System.Security.Cryptography;

namespace TCGCardShopSimModManager.Core;

public sealed class ModInstaller
{
    private readonly JournalStore _journal;
    private readonly ModpackJournalStore _modpackJournal;
    private readonly string _gameFolderPath;
    private readonly string _disabledRoot;

    /// <summary>
    /// Where disabled mods are parked while turned off. Lives beside the mod
    /// manager's own executable — NOT inside the game folder — so the game stays
    /// clean and BepInEx never loads the files. The folder is created on demand
    /// when a mod is first disabled. Defaults to <see cref="DisabledRoot"/>.
    /// Tests pass an explicit path so disabled mods stay inside the test's
    /// scratch folder and never touch the real install.
    /// </summary>
    public ModInstaller(string gameFolderPath, string? disabledRoot = null)
    {
        _gameFolderPath = gameFolderPath;
        _journal = new JournalStore(gameFolderPath);
        _modpackJournal = new ModpackJournalStore(gameFolderPath);
        _disabledRoot = disabledRoot ?? DisabledRoot;
    }

    /// <summary>
    /// The default home for disabled mods: a folder next to this executable.
    /// Returning an absolute path means discovery and the installer agree on
    /// where disabled files live without the game folder being involved.
    /// </summary>
    public static string DisabledRoot =>
        Path.Combine(AppContext.BaseDirectory, "cardshopmodmanager-disabled");

    /// <summary>
    /// Build the file-by-file plan for a mod: verify the source hash, extract it
    /// into <paramref name="extractionRoot"/> (safely), and classify the layout.
    /// Throws on hash mismatch, corrupt archive, or an archive with nothing to install.
    /// </summary>
    public InstallPlan CreatePlan(ModEntry mod, string sourceDirectory, string extractionRoot,
        ArchiveProtectionSettings? settings = null)
    {
        var sourcePath = Path.Combine(sourceDirectory, mod.Archive);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Source file not found: {sourcePath}");

        var sourceHash = ComputeSha256(sourcePath);
        if (!sourceHash.Equals(mod.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Hash mismatch for {mod.Archive}: expected {mod.Sha256}, got {sourceHash}");

        if (ArchiveExtractor.IsSupportedArchive(sourcePath))
        {
            var result = ArchiveExtractor.Extract(sourcePath, extractionRoot, settings ?? ArchiveProtectionSettings.Default);
            if (result.Truncated)
            {
                var detail = result.RejectedEntries.Count > 0
                    ? string.Join("; ", result.RejectedEntries)
                    : "extraction stopped early";
                throw new InvalidDataException(
                    $"{mod.Archive}: extraction was truncated ({detail}) — refusing to install a partial copy.");
            }

            if (result.Sources.Count == 0)
            {
                var detail = result.RejectedEntries.Count > 0
                    ? string.Join("; ", result.RejectedEntries)
                    : "the archive is empty";
                throw new InvalidDataException($"{mod.Archive}: nothing could be extracted ({detail}).");
            }

            return new ArchiveClassifier().BuildPlan(mod, result.Sources, result.RejectedEntries);
        }

        // A plain loose file (e.g. a bare DLL) is treated as a one-file mod.
        var looseSource = new List<ExtractedSource> { new(mod.Archive, sourcePath) };
        return new ArchiveClassifier().BuildPlan(mod, looseSource);
    }

    public InstallResult Install(ModEntry mod, string sourceDirectory)
    {
        if (mod.InstallType != "BepInExPlugin" && mod.InstallType != ModListConventions.BepInExInstallType)
            return new InstallResult(false, $"Unsupported install type: {mod.InstallType}", null);

        var workDir = Path.Combine(Path.GetTempPath(), "cardshopmodmanager-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var installedPaths = new List<string>();
        try
        {
            var plan = CreatePlan(mod, sourceDirectory, workDir);
            var rejected = plan.RejectedEntries;
            var skipped = plan.SkippedEntries;

            if (plan.Files.Count == 0)
                return new InstallResult(false,
                    $"{mod.Archive}: nothing to install (all content was documentation/OS junk)", null,
                    rejected, skipped);

            // Never silently overwrite. Also reject two sources mapping to one destination.
            var existing = plan.Files
                .Where(f => File.Exists(PhysicalPath(_gameFolderPath, f.DestinationRelativePath)))
                .Select(f => f.DestinationRelativePath)
                .ToList();
            if (existing.Count > 0)
                return new InstallResult(false,
                    $"{mod.Archive}: destination already exists, refusing to overwrite: {existing[0]}", null);

            var duplicate = plan.Files
                .GroupBy(f => f.DestinationRelativePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                return new InstallResult(false,
                    $"{mod.Archive}: multiple files map to the same destination: {duplicate.Key}", null);

            foreach (var file in plan.Files)
            {
                var destinationPath = PhysicalPath(_gameFolderPath, file.DestinationRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                // Copy, then verify the copy landed intact before trusting it.
                File.Copy(file.SourceAbsolutePath, destinationPath);
                if (!HashesMatch(file.SourceAbsolutePath, destinationPath))
                    throw new IOException($"Verification failed after copying {file.DestinationRelativePath}");

                installedPaths.Add(destinationPath);
            }

            // Hash each installed file so uninstall can later refuse to delete
            // anything that has been changed. Remember the pack id (if any) so
            // uninstall can clear a now-empty pack from the pack journal (BUG-005).
            _journal.Add(new InstallJournalEntry(
                plan.Mod.Name,
                DateTimeOffset.UtcNow,
                installedPaths.Select(p => new JournalFileEntry(p, ComputeSha256(p))).ToList())
                with { PackId = mod.PackId });

            return new InstallResult(true, null, installedPaths, rejected, skipped);
        }
        catch (Exception ex)
        {
            // Roll back this install: delete exactly what this call created.
            foreach (var path in installedPaths)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // Best effort; the journal was never written so nothing claims
                    // these files were installed.
                }
            }

            return new InstallResult(false, $"Install failed: {ex.Message}", null);
        }
        finally
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);
        }
    }

    /// <summary>
    /// True when a mod is journaled as installed and every file it installed
    /// still exists. A stale journal entry (files manually deleted) counts as
    /// not installed, so a later install can restore it.
    /// </summary>
    public bool IsInstalled(string modName)
    {
        var entry = _journal.Load().FirstOrDefault(e => e.ModName == modName);
        return entry is not null && entry.Files.All(f => File.Exists(f.Path));
    }

    /// <summary>
/// Disable a mod without deleting anything: move every journaled file that sits
/// under BepInEx/plugins or BepInEx/patchers into the manager's disabled
/// folder (beside the executable), preserving the tree. The move is reversible
/// via <see cref="Enable"/>. Files that were modified since install are left in
/// place with a warning rather than touched.
/// </summary>
public DisableResult Disable(string modName)
{
    var warnings = new List<string>();
    var entry = _journal.Load().FirstOrDefault(e => e.ModName == modName);
    if (entry is null)
        return new DisableResult(false, $"No journal entry found for {modName}", warnings);

    var moved = 0;
    var alreadyDisabled = 0;
    var kept = 0;
    var nonManaged = 0;

    foreach (var file in entry.Files)
    {
        var sections = ManagedSections(file.Path);
        if (sections is null)
        {
            // BUG-011: framework/core and game-root files are not something we
            // toggle here; counting them lets us report a proper non-success.
            warnings.Add($"Not a managed BepInEx file, skipping: {file.Path}");
            nonManaged++;
            continue;
        }

        if (!File.Exists(file.Path))
        {
            warnings.Add($"Already missing, skipping: {file.Path}");
            alreadyDisabled++;
            continue;
        }

        if (!HashMatchesCurrent(file.Path, file.Sha256))
        {
            warnings.Add($"Modified since install, keeping in place: {file.Path}");
            kept++;
            continue;
        }

        var disabledPath = _disabledRoot;
        foreach (var segment in sections)
            disabledPath = Path.Combine(disabledPath, segment);

        Directory.CreateDirectory(Path.GetDirectoryName(disabledPath)!);

        // BUG-016: a stale disabled copy (e.g. disabled -> reinstalled -> disable
        // again) would make File.Move throw "file already exists". Clear it first.
        if (File.Exists(disabledPath))
        {
            try
            {
                File.Delete(disabledPath);
            }
            catch
            {
                warnings.Add($"Could not clear stale disabled copy, leaving enabled: {disabledPath}");
                continue;
            }
        }

        File.Move(file.Path, disabledPath);
        moved++;
    }

    PruneEmptyActiveFolders();

    // BUG-013: at least one managed file was kept — the mod is still partially active.
    if (moved > 0 && kept > 0)
        return new DisableResult(false,
            $"{modName} is only partially disabled: {kept} file(s) modified since install were left active, so the mod is still partially loaded.",
            warnings);

    if (moved == 0)
    {
        if (nonManaged > 0)
            // BUG-011: framework/game-root mods aren't something we toggle here.
            return new DisableResult(false,
                $"{modName} is not a managed BepInEx/plugins or BepInEx/patchers mod; framework/game-root mods cannot be disabled here.",
                warnings);

        if (kept > 0)
            return new DisableResult(false,
                $"{modName}: nothing disabled — {kept} file(s) modified since install were left in place.",
                warnings);

        // BUG-018: idempotent no-op — it was already disabled.
        return new DisableResult(true, null, warnings, $"Already disabled: {modName}");
    }

    return new DisableResult(true, null, warnings);
}

/// <summary>
/// Reverse of <see cref="Disable"/>: move journaled files that sit in the
/// disabled folder back to their original paths. Refuses the restore if
/// something already occupies the destination.
/// </summary>
public EnableResult Enable(string modName)
{
    var warnings = new List<string>();
    var entry = _journal.Load().FirstOrDefault(e => e.ModName == modName);
    if (entry is null)
        return new EnableResult(false, $"No journal entry found for {modName}", warnings);

    var moved = 0;
    var alreadyEnabled = 0;
    var nonManaged = 0;

    foreach (var file in entry.Files)
    {
        var sections = ManagedSections(file.Path);
        if (sections is null)
        {
            // BUG-011: framework/core and game-root files are not toggled here.
            warnings.Add($"Not a managed BepInEx file, skipping: {file.Path}");
            nonManaged++;
            continue;
        }

        var disabledPath = _disabledRoot;
        foreach (var segment in sections)
            disabledPath = Path.Combine(disabledPath, segment);

        if (!File.Exists(disabledPath))
        {
            warnings.Add($"Not in the disabled folder, skipping: {Path.GetFileName(file.Path)}");
            alreadyEnabled++;
            continue;
        }

        if (File.Exists(file.Path))
        {
            warnings.Add($"Destination already exists, leaving disabled: {file.Path}");
            continue;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        File.Move(disabledPath, file.Path);
        moved++;
    }

    PruneEmptyDisabledFolders();

    if (moved == 0)
    {
        if (nonManaged > 0)
            // BUG-011: framework/game-root mods aren't something we toggle here.
            return new EnableResult(false,
                $"{modName} is not a managed BepInEx/plugins or BepInEx/patchers mod; framework/game-root mods cannot be enabled here.",
                warnings);

        // BUG-018: idempotent no-op — it was already enabled.
        return new EnableResult(true, null, warnings, $"Already enabled: {modName}");
    }

    return new EnableResult(true, null, warnings);
}

/// <summary>
/// The part of a journaled path that lives under a managed root (plugins or
/// patchers), e.g. ["ModName", "lib", "file.dll"], so it can be relocated to the
/// disabled folder and back. Null when the file isn't one we manage.
/// </summary>
private string[]? ManagedSections(string filePath)
{
    var relative = RelativeToGame(filePath);
    if (relative is null)
        return null;

    var sections = relative.Replace('\\', '/').Split('/');
    if (sections.Length < 3 ||
        !sections[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase) ||
        !(sections[1].Equals("plugins", StringComparison.OrdinalIgnoreCase) ||
          sections[1].Equals("patchers", StringComparison.OrdinalIgnoreCase)))
        return null;

    return sections.Skip(2).ToArray();
}

private string? RelativeToGame(string filePath)
{
    var full = Path.GetFullPath(filePath);
    var game = Path.GetFullPath(_gameFolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return full.StartsWith(game, StringComparison.OrdinalIgnoreCase) ? full[game.Length..] : null;
}

private bool HashMatchesCurrent(string path, string expectedSha256) =>
    ComputeSha256(path).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);

private void PruneEmptyDisabledFolders()
{
    var disabledRoot = _disabledRoot;
    if (!Directory.Exists(disabledRoot))
        return;

    try
    {
        foreach (var folder in Directory.EnumerateDirectories(disabledRoot))
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
    }
    catch
    {
        // Best effort cleanup of emptied folders.
    }
}

private void PruneEmptyActiveFolders()
{
    foreach (var root in new[] { "BepInEx/plugins", "BepInEx/patchers" })
    {
        var fullRoot = Path.Combine(_gameFolderPath, root.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(fullRoot))
            continue;

        foreach (var folder in Directory.EnumerateDirectories(fullRoot))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
            catch
            {
                // Best effort cleanup of emptied folders.
            }
        }
    }
}

public UninstallResult Uninstall(string modName)
    {
    // BUG-040: a missing game folder is distinct from "no journal entry".
    if (!Directory.Exists(_gameFolderPath))
        return new UninstallResult(false, $"Game folder not found: {_gameFolderPath}", new List<string>());

    var entries = _journal.Load();
    var entry = entries.FirstOrDefault(e => e.ModName == modName);

    if (entry is null)
        return new UninstallResult(false, $"No journal entry found for {modName}", new List<string>());

    var warnings = new List<string>();
    var deleted = 0;
    var kept = 0;

    foreach (var file in entry.Files)
    {
        if (!File.Exists(file.Path))
        {
            warnings.Add($"File already missing, skipping: {file.Path}");
            continue;
        }

        var currentHash = ComputeSha256(file.Path);
        if (!currentHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"File was modified since install, refusing to delete: {file.Path}");
            kept++;
            continue;
        }

        File.Delete(file.Path);
        deleted++;
    }

    // BUG-014: only drop the journal entry when every file was actually removed.
    // If a file was kept (modified), the mod is still on disk and must stay tracked
    // so it can be cleaned up later instead of being silently stranded.
    if (kept == 0 && deleted > 0)
    {
        _journal.Remove(modName);

        // BUG-005: if this was the last journaled mod belonging to a pack, clear
        // the stale pack entry so update detection stops reporting "Update available".
        if (!string.IsNullOrEmpty(entry.PackId) &&
            !entries.Any(e => !ReferenceEquals(e, entry) &&
                              string.Equals(e.PackId, entry.PackId, StringComparison.OrdinalIgnoreCase)))
        {
            try { _modpackJournal.Remove(entry.PackId!); }
            catch { /* best effort; pack journal is advisory */ }
        }
    }
    else if (kept > 0)
    {
        warnings.Add($"Uninstall incomplete for {modName}: {kept} file(s) were modified and kept; the journal entry is retained.");
    }

    return new UninstallResult(true, null, warnings);
}

    /// <summary>
    /// Turn a forward-slash relative destination (as ZIP stores it) into a real
    /// absolute path on this OS, using the platform's directory separator.
    /// </summary>
    private static string PhysicalPath(string gameFolderPath, string relativePath) =>
        Path.Combine(gameFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool HashesMatch(string first, string second) =>
        ComputeSha256(first).Equals(ComputeSha256(second), StringComparison.OrdinalIgnoreCase);
}