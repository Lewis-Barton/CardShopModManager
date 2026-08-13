namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Compares modpack version strings (e.g. "1.0.0", "v1.2.0", "1.3.0-beta").
/// Tolerant of a leading "v", a trailing "-prerelease"/"+build" label, and
/// differing component counts (so "1.0" and "1.0.0" are the same version, not a
/// spurious update). Unparseable input is treated as "not newer", so a garbled
/// string never spuriously flags an update.
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

        var a = Normalize(installed);
        var b = Normalize(latest);
        if (a is null || b is null)
            return false;

        return Compare(a, b) < 0;
    }

    /// <summary>
    /// Parse a version into its four numeric components, tolerating a leading
    /// "v" and a trailing "-prerelease"/"+build" label, with missing components
    /// defaulting to 0 (BUG-006/BUG-007). Returns null when no numeric version
    /// can be extracted.
    /// </summary>
    private static int[]? Normalize(string version)
    {
        var v = version.Trim();
        if (v.Length == 0)
            return null;

        // Drop a leading "v"/"V" (e.g. "v1.2.0").
        if (v[0] is 'v' or 'V')
            v = v[1..];

        // Drop any "-prerelease" or "+build" metadata — compare only the numeric
        // components (BUG-006).
        var cut = v.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
            v = v[..cut];

        if (v.Length == 0)
            return null;

        var parts = v.Split('.');
        var components = new int[4];
        for (var i = 0; i < parts.Length && i < 4; i++)
        {
            // A non-numeric component makes the whole version unparseable rather
            // than silently treated as 0.
            if (!int.TryParse(parts[i], out var n))
                return null;
            components[i] = n;
        }

        return components;
    }

    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < 4; i++)
        {
            var c = a[i].CompareTo(b[i]);
            if (c != 0)
                return c;
        }
        return 0;
    }
}
