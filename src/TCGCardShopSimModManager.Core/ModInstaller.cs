using System.Security.Cryptography;

namespace TCGCardShopSimModManager.Core;

public sealed class ModInstaller
{
    private readonly JournalStore _journal;
    private readonly string _gameFolderPath;

    public ModInstaller(string gameFolderPath)
    {
        _gameFolderPath = gameFolderPath;
        _journal = new JournalStore(gameFolderPath);
    }

    /// <summary>
    /// Build the file-by-file plan for a mod: verify the source hash, extract it
    /// into <paramref name="extractionRoot"/> (safely), and classify the layout.
    /// Throws on hash mismatch, corrupt archive, or an archive with nothing to install.
    /// </summary>
    public InstallPlan CreatePlan(ModEntry mod, string sourceDirectory, string extractionRoot)
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
            var result = ArchiveExtractor.Extract(sourcePath, extractionRoot);
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
        if (mod.InstallType != "BepInExPlugin")
            return new InstallResult(false, $"Unsupported install type: {mod.InstallType}", null);

        var workDir = Path.Combine(Path.GetTempPath(), "cardshopmodmanager-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var installedPaths = new List<string>();
        try
        {
            var plan = CreatePlan(mod, sourceDirectory, workDir);

            if (plan.Files.Count == 0)
                return new InstallResult(false,
                    $"{mod.Archive}: nothing to install (all content was documentation/OS junk)", null);

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
            // anything that has been changed.
            _journal.Add(new InstallJournalEntry(
                plan.Mod.Name,
                DateTimeOffset.UtcNow,
                installedPaths.Select(p => new JournalFileEntry(p, ComputeSha256(p))).ToList()));

            return new InstallResult(true, null, installedPaths);
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
/// under BepInEx/plugins or BepInEx/patchers into BepInEx/disabled, preserving
/// the tree. The move is reversible via <see cref="Enable"/>. Files that were
/// modified since install are left in place with a warning rather than touched.
/// </summary>
public DisableResult Disable(string modName)
{
    var warnings = new List<string>();
    var entry = _journal.Load().FirstOrDefault(e => e.ModName == modName);
    if (entry is null)
        return new DisableResult(false, $"No journal entry found for {modName}", warnings);

    foreach (var file in entry.Files)
    {
        var sections = ManagedSections(file.Path);
        if (sections is null)
        {
            warnings.Add($"Not a managed BepInEx file, skipping: {file.Path}");
            continue;
        }

        if (!File.Exists(file.Path))
        {
            warnings.Add($"Already missing, skipping: {file.Path}");
            continue;
        }

        if (!HashMatchesCurrent(file.Path, file.Sha256))
        {
            warnings.Add($"Modified since install, keeping in place: {file.Path}");
            continue;
        }

        var disabledPath = Path.Combine(_gameFolderPath, "BepInEx", "disabled");
        foreach (var segment in sections)
            disabledPath = Path.Combine(disabledPath, segment);

        Directory.CreateDirectory(Path.GetDirectoryName(disabledPath)!);
        File.Move(file.Path, disabledPath);
    }

    PruneEmptyActiveFolders();
    return new DisableResult(true, null, warnings);
}

/// <summary>
/// Reverse of <see cref="Disable"/>: move journaled files that sit in
/// BepInEx/disabled back to their original paths. Refuses the restore if
/// something already occupies the destination.
/// </summary>
public EnableResult Enable(string modName)
{
    var warnings = new List<string>();
    var entry = _journal.Load().FirstOrDefault(e => e.ModName == modName);
    if (entry is null)
        return new EnableResult(false, $"No journal entry found for {modName}", warnings);

    foreach (var file in entry.Files)
    {
        var sections = ManagedSections(file.Path);
        if (sections is null)
        {
            warnings.Add($"Not a managed BepInEx file, skipping: {file.Path}");
            continue;
        }

        var disabledPath = Path.Combine(_gameFolderPath, "BepInEx", "disabled");
        foreach (var segment in sections)
            disabledPath = Path.Combine(disabledPath, segment);

        if (!File.Exists(disabledPath))
        {
            warnings.Add($"Not in the disabled folder, skipping: {Path.GetFileName(file.Path)}");
            continue;
        }

        if (File.Exists(file.Path))
        {
            warnings.Add($"Destination already exists, leaving disabled: {file.Path}");
            continue;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        File.Move(disabledPath, file.Path);
    }

    PruneEmptyDisabledFolders();
    return new EnableResult(true, null, warnings);
}

/// <summary>
/// The part of a journaled path that lives under a managed root (plugins or
/// patchers), e.g. ["ModName", "lib", "file.dll"], so it can be relocated under
/// BepInEx/disabled and back. Null when the file isn't one we manage.
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
    var disabledRoot = Path.Combine(_gameFolderPath, "BepInEx", "disabled");
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
        var entries = _journal.Load();
        var entry = entries.FirstOrDefault(e => e.ModName == modName);

        if (entry is null)
            return new UninstallResult(false, $"No journal entry found for {modName}", new List<string>());

        var warnings = new List<string>();

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
                continue;
            }

            File.Delete(file.Path);
        }

        _journal.Remove(modName);

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