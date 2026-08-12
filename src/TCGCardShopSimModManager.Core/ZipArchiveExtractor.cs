using System.IO.Compression;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// ZIP support using the built-in .NET library. Every entry passes through the
/// protection rules before a single byte is written to disk.
/// </summary>
public sealed class ZipArchiveExtractor : IArchiveExtractor
{
    public string FileExtension => ".zip";

    public ExtractionResult Extract(string archivePath, string destinationDirectory, ArchiveProtectionSettings settings)
    {
        var sources = new List<ExtractedSource>();
        var rejected = new List<string>();
        var totalBytes = 0L;
        var entryCount = 0;

        using var archive = ZipFile.OpenRead(archivePath);

        foreach (var entry in archive.Entries)
        {
            entryCount++;
            if (entryCount > settings.MaxEntries)
            {
                rejected.Add("Entry limit exceeded; extraction stopped.");
                break;
            }

            // ZIP spec uses forward slashes, but a hostile archive may use
            // backslashes to try to sneak past checks — normalize first.
            var relativePath = entry.FullName.Replace('\\', '/');

            // Names ending in "/" are directory entries; folders are created
            // implicitly when the files inside them land, so skip them.
            if (relativePath.EndsWith("/") || entry.Name.Length == 0)
                continue;

            if (IsSymbolicLink(entry))
            {
                rejected.Add($"{relativePath}: symbolic-link entry rejected");
                continue;
            }

            if (!IsSafeRelativePath(relativePath))
            {
                rejected.Add($"{relativePath}: unsafe path rejected");
                continue;
            }

            if (settings.RejectedFileExtensions.Contains(Path.GetExtension(relativePath)))
            {
                rejected.Add($"{relativePath}: unexpected executable rejected");
                continue;
            }

            if (entry.Length > settings.MaxSingleFileBytes)
            {
                rejected.Add($"{relativePath}: single file too large ({entry.Length} bytes)");
                continue;
            }

            totalBytes += entry.Length;
            if (totalBytes > settings.MaxTotalBytes)
            {
                rejected.Add("Total extracted size exceeds limit; extraction stopped.");
                break;
            }

            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: false);
            }
            catch (IOException)
            {
                // A duplicate entry targeting the same destination.
                rejected.Add($"{relativePath}: duplicate or conflicting entry rejected");
                continue;
            }

            sources.Add(new ExtractedSource(relativePath, destinationPath));
        }

        return new ExtractionResult(sources, rejected);
    }

    /// <summary>
    /// Reject Unix symlinks. ZIP keeps the author's Unix "st_mode" in the top
    /// 16 bits of the entry's external attributes; S_IFLNK (symbolic link) is
    /// 0xA000. A symlink in an archive is dangerous because following it can
    /// point writes outside the extraction folder.
    /// </summary>
    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        fileTypeEquals(entry, 0xA000u);

    private static bool fileTypeEquals(ZipArchiveEntry entry, uint typeCode)
    {
        var unixMode = (uint)(entry.ExternalAttributes >> 16);
        return (unixMode & 0xF000u) == typeCode;
    }

    /// <summary>
    /// Reject anything that could escape the extraction folder: rooted paths
    /// ("C:\...", "\...") and any "../" or empty path segment.
    /// </summary>
    private static bool IsSafeRelativePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return false;

        foreach (var segment in relativePath.Split('/'))
        {
            if (segment is ".." or "")
                return false;
        }

        return true;
    }
}