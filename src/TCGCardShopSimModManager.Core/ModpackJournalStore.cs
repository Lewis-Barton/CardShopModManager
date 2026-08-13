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

        try
        {
            var json = File.ReadAllText(_journalPath);
            return JsonSerializer.Deserialize<List<InstalledModpack>>(json, Options)
                   ?? new List<InstalledModpack>();
        }
        catch (JsonException)
        {
            // BUG-004: a corrupt pack journal must never abort an otherwise
            // successful install/upgrade. Back it up and recover to empty.
            BackUpCorrupt(_journalPath);
            return new List<InstalledModpack>();
        }
    }

    /// <summary>Record (or replace) the installed pack version.</summary>
    public void Record(string packId, string packVersion, string name)
    {
        var entries = Load();
        entries.RemoveAll(e => e.PackId.Equals(packId, StringComparison.OrdinalIgnoreCase));
        entries.Add(new InstalledModpack(packId, packVersion, name, DateTimeOffset.UtcNow));
        Save(entries);
    }

    /// <summary>Drop a pack from the journal. Used when the last mod of a pack is uninstalled so a stale "Update available" badge does not linger. (BUG-005)</summary>
    public void Remove(string packId)
    {
        var entries = Load();
        entries.RemoveAll(e => e.PackId.Equals(packId, StringComparison.OrdinalIgnoreCase));
        Save(entries);
    }

    private void Save(List<InstalledModpack> entries)
    {
        var json = JsonSerializer.Serialize(entries, Options);
        AtomicWrite(_journalPath, json);
    }

    /// <summary>Write atomically (temp file + rename) and keep a .bak of the previous good content. (BUG-010)</summary>
    private static void AtomicWrite(string path, string json)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tmp = path + ".tmp";
        if (File.Exists(path))
        {
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { /* best effort */ }
        }
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Move a corrupt pack journal aside to <c>&lt;journal&gt;.corrupt</c> so it can be inspected.</summary>
    private static void BackUpCorrupt(string path)
    {
        try
        {
            var corrupt = path + ".corrupt";
            if (File.Exists(corrupt)) File.Delete(corrupt);
            File.Move(path, corrupt);
        }
        catch
        {
            // If we cannot move it, the empty list we return is what matters.
        }
    }
}
