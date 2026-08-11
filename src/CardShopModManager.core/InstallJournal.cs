namespace CardShopModManager.Core;

public sealed record JournalFileEntry(string Path, string Sha256);

public sealed record InstallJournalEntry(
    string ModName,
    DateTimeOffset InstalledAt,
    List<JournalFileEntry> Files);