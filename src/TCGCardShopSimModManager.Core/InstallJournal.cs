namespace TCGCardShopSimModManager.Core;

public sealed record JournalFileEntry(
    string Path,
    string Sha256,
    /// <summary>True when the file already existed with identical content and
    /// was adopted for tracking. The manager must never delete or replace it.</summary>
    bool PreserveOnUninstall = false);

public sealed record InstallJournalEntry(
    string ModName,
    DateTimeOffset InstalledAt,
    List<JournalFileEntry> Files,
    string? PackId = null,
    string? ModId = null,
    string? Version = null,
    string? ArchiveSha256 = null);
