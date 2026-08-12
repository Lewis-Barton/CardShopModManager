namespace TCGCardShopSimModManager.Core;

/// <summary>
/// A contract for reading a file archive safely (one implementation per format).
/// Extraction is only ever allowed into a temporary folder we control, never
/// directly into the game.
/// </summary>
public interface IArchiveExtractor
{
    /// <summary>The file extension this extractor handles, e.g. ".zip".</summary>
    string FileExtension { get; }

    /// <summary>
    /// Extract the archive into <paramref name="destinationDirectory"/> under the
    /// protection rules in <paramref name="settings"/>. Returns what was extracted
    /// and what was rejected (traversal, symlinks, executables, oversized files...).
    /// Throws if the archive itself is corrupt.
    /// </summary>
    ExtractionResult Extract(string archivePath, string destinationDirectory, ArchiveProtectionSettings settings);
}