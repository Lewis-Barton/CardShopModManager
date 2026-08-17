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
    private readonly string _gameFolderPath;

    public JournalStore(string gameFolderPath)
    {
        _gameFolderPath = Path.GetFullPath(gameFolderPath);
        _file = new AtomicJsonFile<List<InstallJournalEntry>>(
            Path.Combine(gameFolderPath, "cardshopmodmanager.journal.json"),
            Options, () => new List<InstallJournalEntry>(), recoverCorrupt: true);
    }

    public List<InstallJournalEntry> Load()
    {
        return Validate(_file.Read());
    }

    public void Save(List<InstallJournalEntry> entries)
    {
        _file.Write(Validate(entries));
    }

    public void Add(InstallJournalEntry entry)
    {
        _file.Update(entries =>
        {
            Validate(entries);
            Validate([entry]);
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
            Validate(entries);
            entries.RemoveAll(e => e.ModName == modName);
            return (entries, true);
        });
    }

    private List<InstallJournalEntry> Validate(List<InstallJournalEntry> entries)
    {
        var prefix = Path.EndsInDirectorySeparator(_gameFolderPath)
            ? _gameFolderPath
            : _gameFolderPath + Path.DirectorySeparatorChar;

        foreach (var entry in entries)
        foreach (var file in entry.Files)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(file.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new InvalidDataException(
                    $"Install journal contains an invalid path for {entry.ModName}.", ex);
            }

            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Install journal path for {entry.ModName} escapes the game folder: {file.Path}");

            PathSafety.EnsureContainedWithoutReparsePoints(
                _gameFolderPath, fullPath, $"Install journal path for {entry.ModName}");
        }

        return entries;
    }
}
