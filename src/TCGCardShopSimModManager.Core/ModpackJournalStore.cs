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

    private readonly string _journalPath;

    public ModpackJournalStore(string gameFolderPath)
    {
        _journalPath = Path.Combine(gameFolderPath, "cardshopmodmanager.modpacks.json");
    }

    public List<InstalledModpack> Load()
    {
        if (!File.Exists(_journalPath))
            return new List<InstalledModpack>();

        var json = File.ReadAllText(_journalPath);
        return JsonSerializer.Deserialize<List<InstalledModpack>>(json, Options)
               ?? new List<InstalledModpack>();
    }

    /// <summary>Record (or replace) the installed pack version.</summary>
    public void Record(string packId, string packVersion, string name)
    {
        var entries = Load();
        entries.RemoveAll(e => e.PackId.Equals(packId, StringComparison.OrdinalIgnoreCase));
        entries.Add(new InstalledModpack(packId, packVersion, name, DateTimeOffset.UtcNow));
        Save(entries);
    }

    private void Save(List<InstalledModpack> entries)
    {
        var json = JsonSerializer.Serialize(entries, Options);
        File.WriteAllText(_journalPath, json);
    }
}
