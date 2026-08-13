using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

public sealed class JournalStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _journalPath;

    public JournalStore(string gameFolderPath)
    {
        _journalPath = Path.Combine(gameFolderPath, "cardshopmodmanager.journal.json");
    }

    public List<InstallJournalEntry> Load()
    {
        if (!File.Exists(_journalPath))
            return new List<InstallJournalEntry>();

        try
        {
            var json = File.ReadAllText(_journalPath);
            return JsonSerializer.Deserialize<List<InstallJournalEntry>>(json, Options)
                   ?? new List<InstallJournalEntry>();
        }
        catch (JsonException)
        {
            // BUG-015: a corrupt journal must not abort every lifecycle operation.
            // Back the bad file up for inspection and start from a clean slate so
            // install/disable/enable/uninstall can still proceed.
            BackUpCorrupt(_journalPath);
            return new List<InstallJournalEntry>();
        }
    }

    public void Save(List<InstallJournalEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, Options);
        AtomicWrite(_journalPath, json);
    }

    public void Add(InstallJournalEntry entry)
    {
        var entries = Load();
        entries.RemoveAll(e =>
            (!string.IsNullOrWhiteSpace(entry.ModId) &&
             !string.IsNullOrWhiteSpace(e.ModId) &&
             e.ModId.Equals(entry.ModId, StringComparison.OrdinalIgnoreCase)) ||
            (string.IsNullOrWhiteSpace(e.ModId) &&
             e.ModName.Equals(entry.ModName, StringComparison.OrdinalIgnoreCase)));
        entries.Add(entry);
        Save(entries);
    }

    public void Remove(string modName)
    {
        var entries = Load();
        entries.RemoveAll(e => e.ModName == modName);
        Save(entries);
    }

    /// <summary>Write atomically (temp file + rename) and keep a .bak of the
    /// previous good content, so a crash mid-write cannot leave an unreadable journal. (BUG-010)</summary>
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

    /// <summary>Move a corrupt journal aside to <c>&lt;journal&gt;.corrupt</c> so it can be inspected, keeping operations alive.</summary>
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
