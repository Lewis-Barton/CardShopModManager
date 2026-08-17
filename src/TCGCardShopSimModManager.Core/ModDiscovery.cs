namespace TCGCardShopSimModManager.Core;

public enum ModInventoryState
{
    /// <summary>Present in the game folder but never journaled by us (installed by hand, or by another tool).</summary>
    Unknown,
    /// <summary>In the game folder and matching the journal.</summary>
    Installed,
    /// <summary>Moved to the disabled folder by us; the journal still holds its original paths.</summary>
    Disabled,
    /// <summary>A file differs from the journal (tampered, updated by hand, or partially deleted).</summary>
    Modified
}

public sealed record DiscoveredMod(
    string ModName,
    ModInventoryState State,
    int FileCount,
    string? ActiveRoot);

/// <summary>
/// Builds inventory from journal ownership first, then adds physical content
/// which no journal claims. This keeps one managed mod together even when its
/// files span several roots, while unmanaged folders retain their locations so
/// same-named folders are never silently merged.
/// </summary>
public static class ModDiscovery
{
    private static readonly (string Relative, string Label)[] FolderRoots =
    {
        ("BepInEx/plugins", "BepInEx/plugins"),
        ("BepInEx/patchers", "BepInEx/patchers")
    };

    public static List<DiscoveredMod> Discover(string gameFolderPath, string? disabledRoot = null)
    {
        disabledRoot ??= ModInstaller.DisabledRoot;
        var journal = new JournalStore(gameFolderPath).Load();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discovered = new List<DiscoveredMod>();

        foreach (var entry in journal)
        {
            foreach (var file in entry.Files)
            {
                claimed.Add(Normalize(file.Path));
                if (DisabledPath(file.Path, gameFolderPath, disabledRoot) is { } disabledPath)
                    claimed.Add(Normalize(disabledPath));
            }

            discovered.Add(FromJournal(entry, gameFolderPath, disabledRoot));
        }

        foreach (var (relative, label) in FolderRoots)
            AddUnmanagedFolders(discovered, claimed, Path.Combine(gameFolderPath, ToNative(relative)), label);

        AddUnmanagedFramework(discovered, claimed, gameFolderPath);
        AddUnmanagedFolders(discovered, claimed, disabledRoot, "Disabled storage");

        return discovered
            .OrderBy(mod => mod.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.ActiveRoot, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DiscoveredMod FromJournal(
        InstallJournalEntry entry,
        string gameFolderPath,
        string disabledRoot)
    {
        var active = 0;
        var disabled = 0;
        var modified = entry.Files.Count == 0;

        foreach (var expected in entry.Files)
        {
            var activeExists = File.Exists(expected.Path);
            var disabledPath = DisabledPath(expected.Path, gameFolderPath, disabledRoot);
            var disabledExists = disabledPath is not null && File.Exists(disabledPath);

            if (activeExists && disabledExists)
            {
                modified = true;
                active++;
                disabled++;
                continue;
            }

            if (activeExists)
            {
                active++;
                modified |= !HashMatches(expected.Path, expected.Sha256);
            }
            else if (disabledExists)
            {
                disabled++;
                modified |= !HashMatches(disabledPath!, expected.Sha256);
            }
            else
            {
                modified = true;
            }
        }

        var state = modified || (active > 0 && disabled > 0)
            ? ModInventoryState.Modified
            : disabled == entry.Files.Count
                ? ModInventoryState.Disabled
                : ModInventoryState.Installed;

        return new DiscoveredMod(entry.ModName, state, active + disabled, JournalLocation(entry, gameFolderPath));
    }

    private static void AddUnmanagedFolders(
        List<DiscoveredMod> discovered,
        HashSet<string> claimed,
        string root,
        string label)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => !claimed.Contains(Normalize(file)))
                .ToList();
            if (files.Count == 0)
                continue;

            discovered.Add(new DiscoveredMod(
                $"{Path.GetFileName(folder)} (unmanaged, {label})",
                ModInventoryState.Unknown,
                files.Count,
                label));
        }

        var looseFiles = Directory.EnumerateFiles(root)
            .Count(file => !claimed.Contains(Normalize(file)));
        if (looseFiles > 0)
        {
            discovered.Add(new DiscoveredMod(
                $"Loose files (unmanaged, {label})",
                ModInventoryState.Unknown,
                looseFiles,
                label));
        }
    }

    private static void AddUnmanagedFramework(
        List<DiscoveredMod> discovered,
        HashSet<string> claimed,
        string gameFolderPath)
    {
        var root = Path.Combine(gameFolderPath, "BepInEx", "core");
        if (!Directory.Exists(root))
            return;

        var count = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Count(file => !claimed.Contains(Normalize(file)));
        if (count > 0)
        {
            discovered.Add(new DiscoveredMod(
                "BepInEx framework files (unmanaged)",
                ModInventoryState.Unknown,
                count,
                "BepInEx/core"));
        }
    }

    private static string JournalLocation(InstallJournalEntry entry, string gameFolderPath)
    {
        var game = Normalize(gameFolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var roots = entry.Files.Select(file =>
        {
            var full = Normalize(file.Path);
            if (!full.StartsWith(game, StringComparison.OrdinalIgnoreCase))
                return "Outside game folder";

            var parts = full[game.Length..].Replace('\\', '/').Split('/');
            if (parts.Length == 1)
                return "Game root";
            if (parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                return $"BepInEx/{parts[1]}";
            return parts[0];
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return roots.Count switch
        {
            0 => "Journal",
            1 => roots[0],
            _ => "Multiple locations"
        };
    }

    private static string? DisabledPath(string filePath, string gameFolderPath, string disabledRoot)
    {
        var game = Normalize(gameFolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Normalize(filePath);
        if (!full.StartsWith(game, StringComparison.OrdinalIgnoreCase))
            return null;

        var sections = full[game.Length..].Replace('\\', '/').Split('/');
        if (sections.Length < 3 ||
            !sections[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase) ||
            !(sections[1].Equals("plugins", StringComparison.OrdinalIgnoreCase) ||
              sections[1].Equals("patchers", StringComparison.OrdinalIgnoreCase)))
            return null;

        return Path.Combine(new[] { disabledRoot }.Concat(sections.Skip(2)).ToArray());
    }

    private static string ToNative(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static string Normalize(string path) => Path.GetFullPath(path);

    private static bool HashMatches(string path, string expected)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
