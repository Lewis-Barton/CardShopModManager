namespace TCGCardShopSimModManager.Core;

public sealed record ModListManifest(
    int ManifestVersion,
    string Name,
    string Game,
    List<ModEntry> Mods,
    /// <summary>Optional total download size in bytes, declared by the pack
    /// author. When present, the installer pre-flights disk space (download temp
    /// + game folder) before fetching anything.</summary>
    long? TotalSize = null);

/// <summary>
/// One mod in the list. <see cref="Id"/> is the stable key dependencies and
/// profiles reference; <see cref="Name"/> is what humans see.
/// </summary>
public sealed record ModEntry(
    string Id,
    string Name,
    string? Version,
    string Archive,
    string Sha256,
    string InstallType,
    List<string> Dependencies,
    List<string> Conflicts,
    long? NexusModId = null,
    long? NexusFileId = null,
    string? DownloadUrl = null);