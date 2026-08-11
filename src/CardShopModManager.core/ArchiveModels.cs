namespace CardShopModManager.Core;

/// <summary>
/// One file that came out of the source (an archive, or a loose file).
/// </summary>
public sealed record ExtractedSource(
    string RelativePath,
    string AbsolutePath);

/// <summary>
/// The result of a protected extraction: what made it out, and what was rejected.
/// </summary>
public sealed record ExtractionResult(
    List<ExtractedSource> Sources,
    List<string> RejectedEntries);

/// <summary>
/// One planned file copy: from where, what it looked like, and where it goes.
/// </summary>
public sealed record ArchiveContentEntry(
    string SourceAbsolutePath,
    string SourceRelativePath,
    string DestinationRelativePath);