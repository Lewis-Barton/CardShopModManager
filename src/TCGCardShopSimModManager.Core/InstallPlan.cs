namespace TCGCardShopSimModManager.Core;

/// <summary>
/// A resolved, file-by-file installation plan for one mod. Pure data — nothing
/// has been written to the game yet, so it can be previewed as a dry run.
/// </summary>
public sealed record InstallPlan(
    ModEntry Mod,
    string LayoutName,
    List<ArchiveContentEntry> Files,
    List<string> SkippedEntries,
    List<string> RejectedEntries);

public sealed record InstallResult(
    bool Success,
    string? Error,
    List<string>? InstalledPaths);

public sealed record UninstallResult(
    bool Success,
    string? Error,
    List<string> Warnings);

public sealed record DisableResult(
    bool Success,
    string? Error,
    List<string> Warnings);

public sealed record EnableResult(
    bool Success,
    string? Error,
    List<string> Warnings);