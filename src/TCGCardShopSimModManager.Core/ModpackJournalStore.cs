using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>One modpack recorded as installed in a game folder.</summary>
public sealed record InstalledModpack(
    string PackId,
    string PackVersion,
    string Name,
    DateTimeOffset InstalledAt);

/// <summary>
/// Tracks which modpacks (and which versions) are installed in a game folder,
/// so the app can tell you when a newer pack is published. Kept in its own file
/// (<c>cardshopmodmanager.modpacks.json</c>) rather than the per-mod install
/// journal, so extending it never risks the existing mod-tracking schema.
/// </summary>
public sealed class ModpackJournalStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AtomicJsonFile<List<InstalledModpack>> _file;

    public ModpackJournalStore(string gameFolderPath)
    {
        _file = new AtomicJsonFile<List<InstalledModpack>>(
            Path.Combine(gameFolderPath, "cardshopmodmanager.modpacks.json"),
            Options, () => new List<InstalledModpack>(), recoverCorrupt: true);
    }

    public List<InstalledModpack> Load()
    {
        return _file.Read();
    }

    /// <summary>Record (or replace) the installed pack version.</summary>
    public void Record(string packId, string packVersion, string name)
    {
        _file.Update(entries =>
        {
            entries.RemoveAll(e => e.PackId.Equals(packId, StringComparison.OrdinalIgnoreCase));
            entries.Add(new InstalledModpack(packId, packVersion, name, DateTimeOffset.UtcNow));
            return (entries, true);
        });
    }

    /// <summary>Drop a pack from the journal. Used when the last mod of a pack is uninstalled so a stale "Update available" badge does not linger. (BUG-005)</summary>
    public void Remove(string packId)
    {
        _file.Update(entries =>
        {
            entries.RemoveAll(e => e.PackId.Equals(packId, StringComparison.OrdinalIgnoreCase));
            return (entries, true);
        });
    }

    /// <summary>Persist the installed-pack list (atomic write, BUG-010).</summary>
    public void Save(List<InstalledModpack> entries)
    {
        _file.Write(entries);
    }
}
