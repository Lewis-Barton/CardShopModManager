namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Compares modpack version strings (e.g. "1.0.0"). Tolerant of 2- or 3-part
/// versions and of unparseable input: a version that doesn't parse is treated as
/// "not newer", so a garbled string never spuriously flags an update.
/// </summary>
public static class ModpackVersion
{
    /// <summary>
    /// True when <paramref name="latest"/> is a newer published version than the
    /// <paramref name="installed"/> one. Returns false when nothing is installed
    /// yet (no "update" to show) or either value can't be parsed.
    /// </summary>
    public static bool IsNewer(string? installed, string latest)
    {
        if (installed is null)
            return false;
        if (!Version.TryParse(installed.Trim(), out var a) || a is null)
            return false;
        if (!Version.TryParse(latest.Trim(), out var b) || b is null)
            return false;
        return a.CompareTo(b) < 0;
    }
}
