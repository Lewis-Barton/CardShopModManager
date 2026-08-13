namespace TCGCardShopSimModManager.Core;

public sealed record JournalFileEntry(string Path, string Sha256);

public sealed record InstallJournalEntry(
    string ModName,
    DateTimeOffset InstalledAt,
    List<JournalFileEntry> Files,
    string? PackId = null,
    string? ModId = null,
    string? Version = null,
    string? ArchiveSha256 = null);
