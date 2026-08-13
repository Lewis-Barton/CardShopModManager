using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

public sealed class JournalStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AtomicJsonFile<List<InstallJournalEntry>> _file;

    public JournalStore(string gameFolderPath)
    {
        _file = new AtomicJsonFile<List<InstallJournalEntry>>(
            Path.Combine(gameFolderPath, "cardshopmodmanager.journal.json"),
            Options, () => new List<InstallJournalEntry>(), recoverCorrupt: true);
    }

    public List<InstallJournalEntry> Load()
    {
        return _file.Read();
    }

    public void Save(List<InstallJournalEntry> entries)
    {
        _file.Write(entries);
    }

    public void Add(InstallJournalEntry entry)
    {
        _file.Update(entries =>
        {
            entries.RemoveAll(e =>
                (!string.IsNullOrWhiteSpace(entry.ModId) &&
                 !string.IsNullOrWhiteSpace(e.ModId) &&
                 e.ModId.Equals(entry.ModId, StringComparison.OrdinalIgnoreCase)) ||
                (string.IsNullOrWhiteSpace(e.ModId) &&
                 e.ModName.Equals(entry.ModName, StringComparison.OrdinalIgnoreCase)));
            entries.Add(entry);
            return (entries, true);
        });
    }

    public void Remove(string modName)
    {
        _file.Update(entries =>
        {
            entries.RemoveAll(e => e.ModName == modName);
            return (entries, true);
        });
    }
}
