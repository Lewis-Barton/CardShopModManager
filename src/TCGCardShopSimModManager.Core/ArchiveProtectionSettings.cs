namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Caps and denylists applied while reading an archive, so a hostile or broken
/// mod cannot write outside the extraction folder or exhaust the disk.
/// </summary>
public sealed record ArchiveProtectionSettings(
    int MaxEntries,
    long MaxSingleFileBytes,
    long MaxTotalBytes,
    IReadOnlySet<string> RejectedFileExtensions)
{
    public static ArchiveProtectionSettings Default { get; } = new(
        MaxEntries: 100000,
        MaxSingleFileBytes: 32L * 1024 * 1024 * 1024, // 32 GiB per file
        MaxTotalBytes: 64L * 1024 * 1024 * 1024,      // 64 GiB per archive
        // Executables and nested archives are both refused: a nested archive
        // would bypass every protection check below, and executables must never
        // be dropped into a game folder.
        RejectedFileExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".cmd", ".bat", ".com", ".scr", ".ps1", ".vbs", ".vbe", ".wsf",
            ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"
        });
}
