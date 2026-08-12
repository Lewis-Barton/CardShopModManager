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
/// What is actually in the game's mod folders right now — read from disk, with
/// the journal used to explain it — rather than trusting the journal alone.
/// A user is not required to have a fresh install.
/// </summary>
public static class ModDiscovery
{
    private static readonly string[] ActiveRoots = { "BepInEx/plugins", "BepInEx/patchers" };
    private const string DisabledRoot = "BepInEx/disabled";

    public static List<DiscoveredMod> Discover(string gameFolderPath)
    {
        var journal = new JournalStore(gameFolderPath).Load();
        var mods = new Dictionary<string, (ModInventoryState State, int Count, string? Root)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var root in ActiveRoots)
        {
            var fullRoot = Path.Combine(gameFolderPath, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullRoot))
                continue;

            foreach (var folder in Directory.EnumerateDirectories(fullRoot))
            {
                var name = Path.GetFileName(folder);
                var count = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count();

                if (!mods.ContainsKey(name))
                    mods[name] = (StateOf(name, folder, journal), count, root);
            }
        }

        var disabledFull = Path.Combine(gameFolderPath, DisabledRoot.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(disabledFull))
        {
            foreach (var folder in Directory.EnumerateDirectories(disabledFull))
            {
                var name = Path.GetFileName(folder);
                var count = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count();

                // An emptied disabled folder is just leftover scaffolding.
                if (count == 0)
                    continue;

                // A mod can be both present in the active tree (leftover files
                // after a partial disable) and in disabled; disabled takes
                // precedence for reporting, but we keep the active root if set.
                if (mods.ContainsKey(name))
                    mods[name] = (ModInventoryState.Disabled, count, mods[name].Root);
                else
                    mods[name] = (ModInventoryState.Disabled, count, DisabledRoot);
            }
        }

        return mods
            .Select(kv => new DiscoveredMod(kv.Key, kv.Value.State, kv.Value.Count, kv.Value.Root))
            .OrderBy(m => m.ModName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A folder in the active tree is Installed if every file matches its
    /// journal hash, Modified if any file differs or a journaled one is
    /// missing, and Unknown if there is no journal entry at all.
    /// </summary>
    private static ModInventoryState StateOf(string modName, string folder, List<InstallJournalEntry> journal)
    {
        var entry = journal.FirstOrDefault(e => e.ModName == modName);
        if (entry is null)
            return ModInventoryState.Unknown;

        var byPath = entry.Files.ToDictionary(f => Normalize(f.Path), StringComparer.OrdinalIgnoreCase);

        var anyModified = false;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!byPath.TryGetValue(Normalize(file), out var expected))
            {
                anyModified = true; // extra file that isn't one we installed
                continue;
            }

            if (!HashMatches(file, expected.Sha256))
                anyModified = true;
        }

        // A journaled file that isn't on disk counts as modified too.
        if (byPath.Values.Any(f => !File.Exists(f.Path)))
            anyModified = true;

        return anyModified ? ModInventoryState.Modified : ModInventoryState.Installed;
    }

    private static string Normalize(string path) => Path.GetFullPath(path);

    private static bool HashMatches(string path, string expected)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}