using System.Text.Json;

namespace CardShopModManager.Core;

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

        var json = File.ReadAllText(_journalPath);
        return JsonSerializer.Deserialize<List<InstallJournalEntry>>(json, Options)
               ?? new List<InstallJournalEntry>();
    }

    public void Save(List<InstallJournalEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, Options);
        File.WriteAllText(_journalPath, json);
    }

    public void Add(InstallJournalEntry entry)
    {
        var entries = Load();
        entries.RemoveAll(e => e.ModName == entry.ModName); // replace if reinstalling
        entries.Add(entry);
        Save(entries);
    }

    public void Remove(string modName)
    {
        var entries = Load();
        entries.RemoveAll(e => e.ModName == modName);
        Save(entries);
    }
}