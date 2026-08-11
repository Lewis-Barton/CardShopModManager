namespace CardShopModManager.Core;

/// <summary>
/// The front door for reading archives: pick the extractor that knows the
/// format, or say this file isn't an archive at all. 7z/RAR will become new
/// implementations of <see cref="IArchiveExtractor"/> without touching the
/// rest of the pipeline.
/// </summary>
public static class ArchiveExtractor
{
    private static readonly IArchiveExtractor[] Extractors =
    {
        new ZipArchiveExtractor()
    };

    public static bool IsSupportedArchive(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return Extractors.Any(e => e.FileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static ExtractionResult Extract(string archivePath, string destinationDirectory)
        => Extract(archivePath, destinationDirectory, ArchiveProtectionSettings.Default);

    public static ExtractionResult Extract(
        string archivePath,
        string destinationDirectory,
        ArchiveProtectionSettings settings)
    {
        var extension = Path.GetExtension(archivePath);
        var extractor = Extractors.FirstOrDefault(
            e => e.FileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"Unsupported archive format: {extension}");

        return extractor.Extract(archivePath, destinationDirectory, settings);
    }
}