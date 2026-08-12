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
        MaxEntries: 4096,
        MaxSingleFileBytes: 512L * 1024 * 1024,  // 512 MiB per file
        MaxTotalBytes: 1024L * 1024 * 1024,      // 1 GiB total
        RejectedFileExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".cmd", ".bat", ".com", ".scr", ".ps1", ".vbs", ".vbe", ".wsf"
        });
}