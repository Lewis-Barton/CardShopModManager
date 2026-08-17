namespace TCGCardShopSimModManager.Core;

public enum GameCompatibilityStatus
{
    Compatible,
    Incompatible,
    InstalledBuildUnknown,
    NotDeclared
}

public sealed record GameCompatibilityResult(
    GameCompatibilityStatus Status,
    string? InstalledBuildId,
    IReadOnlyList<string> CompatibleBuildIds)
{
    public bool MayBeUnsupported => Status != GameCompatibilityStatus.Compatible;
}

public static class GameCompatibility
{
    public static GameCompatibilityResult Evaluate(
        IEnumerable<string>? compatibleBuildIds,
        string? installedBuildId)
    {
        var supported = compatibleBuildIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        if (supported.Length == 0)
            return new GameCompatibilityResult(
                GameCompatibilityStatus.NotDeclared, installedBuildId, supported);

        if (string.IsNullOrWhiteSpace(installedBuildId))
            return new GameCompatibilityResult(
                GameCompatibilityStatus.InstalledBuildUnknown, null, supported);

        var compatible = supported.Contains(installedBuildId, StringComparer.OrdinalIgnoreCase);
        return new GameCompatibilityResult(
            compatible ? GameCompatibilityStatus.Compatible : GameCompatibilityStatus.Incompatible,
            installedBuildId,
            supported);
    }
}
