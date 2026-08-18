namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Shared conventions for modpack manifests. BepInEx is the mod framework every
/// pack depends on; it is always installed first so plugins have something to
/// load into. Packs declare it with this id and install type, and
/// <see cref="ModpackInstaller"/> enforces the ordering at install time.
/// </summary>
public static class ModListConventions
{
    /// <summary>The reserved mod id for the BepInEx framework entry.</summary>
    public const string BepInExModId = "bepinex";

    /// <summary>
    /// Install type for the BepInEx framework itself (as opposed to a plugin that
    /// loads inside it). The classifier still decides the on-disk layout from the
    /// archive contents; this type only tells the installer the entry is allowed.
    /// </summary>
    public const string BepInExInstallType = "BepInEx";
}

public sealed record ModListManifest(
    int ManifestVersion,
    string Name,
    string Game,
    List<ModEntry> Mods,
    /// <summary>Optional total download size in bytes, declared by the pack
    /// author. When present, the installer pre-flights disk space (download temp
    /// + game folder) before fetching anything.</summary>
    long? TotalSize = null,
    /// <summary>Steam build ids this list has been tested against. An absent or
    /// empty list means compatibility has not been declared.</summary>
    List<string>? CompatibleGameBuildIds = null);

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
    string? DownloadUrl = null,
    string? PackId = null,
    /// <summary>Required entries are always installed. Optional entries are
    /// installed only when selected by the user. Defaults to true so existing
    /// manifests retain their current install-all behaviour.</summary>
    bool Required = true,
    /// <summary>Archive-relative files or directory trees this pack deliberately
    /// leaves out. A trailing slash identifies a directory tree; other values
    /// identify one exact file.</summary>
    List<string>? ExcludedArchivePaths = null);
