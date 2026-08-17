using System.Text.RegularExpressions;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Finds where a Steam game is installed without needing an API key: read the
/// Steam install path from the registry, parse the library folders, and look
/// for the game's app manifest to learn its install directory.
/// </summary>
public sealed class SteamLocator
{
    /// <summary>TCG Card Shop Simulator (verified against the Steam store: 3070070).</summary>
    public const int GameAppId = 3070070;

    public const string GameExecutableName = "Card Shop Simulator.exe";

    private const string SteamRegistryHive = @"HKEY_CURRENT_USER\Software\Valve\Steam";
    private const string SteamRegistryValue = "SteamPath";

    private readonly string? _steamPathOverride;

    public SteamLocator(string? steamPathOverride = null)
    {
        _steamPathOverride = steamPathOverride;
    }

    /// <summary>
    /// The folder that contains the game (where the .exe would be), or null when
    /// Steam is not present or the game isn't installed.
    /// </summary>
    public string? FindGameInstallPath(int appId)
    {
        var steamRoot = _steamPathOverride ?? ReadRegistrySteamPath();
        if (string.IsNullOrEmpty(steamRoot) || !Directory.Exists(steamRoot))
            return null;

        var libraries = Libraries(steamRoot);
        if (libraries.Count == 0)
            return null;

        foreach (var library in libraries)
        {
            var steamApps = SteamAppsPath(library);
            var manifestPath = Path.Combine(steamApps, $"appmanifest_{appId}.acf");
            if (!File.Exists(manifestPath))
                continue;

            var installDir = ParseManifestInstallDir(manifestPath);
            if (string.IsNullOrWhiteSpace(installDir))
                continue;

            var gamePath = Path.Combine(steamApps, "common", installDir);
            if (Directory.Exists(gamePath))
                return gamePath;
        }

        return null;
    }

    /// <summary>
    /// Reads the installed Steam build id for a selected game folder. The direct
    /// parent lookup also works when the user browsed to a library that is not
    /// present in Steam's current libraryfolders.vdf.
    /// </summary>
    public string? FindGameBuildId(string gameFolderPath, int appId)
    {
        if (string.IsNullOrWhiteSpace(gameFolderPath))
            return null;

        var fullGamePath = NormalizePath(gameFolderPath);
        var common = Directory.GetParent(fullGamePath);
        var directSteamApps = common?.Parent?.FullName;
        if (directSteamApps is not null &&
            TryReadBuildId(directSteamApps, fullGamePath, appId, out var directBuildId))
            return directBuildId;

        var steamRoot = _steamPathOverride ?? ReadRegistrySteamPath();
        if (string.IsNullOrEmpty(steamRoot) || !Directory.Exists(steamRoot))
            return null;

        foreach (var library in Libraries(steamRoot))
        {
            if (TryReadBuildId(SteamAppsPath(library), fullGamePath, appId, out var buildId))
                return buildId;
        }

        return null;
    }

    public string? ReadRegistrySteamPath()
    {
        // The registry read is Windows-only; Steam detection can only happen
        // on Windows.
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>All Steam library folders, including the primary steamapps folder.</summary>
    public static List<string> Libraries(string steamRoot)
    {
        var libraries = new List<string>();

        var primary = Path.Combine(steamRoot, "steamapps");
        if (Directory.Exists(primary))
            libraries.Add(primary);

        var vdfPath = Path.Combine(primary, "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            foreach (var path in ParseLibraryFolders(vdfPath))
            {
                if (!libraries.Contains(path, StringComparer.OrdinalIgnoreCase))
                    libraries.Add(path);
            }
        }

        return libraries;
    }

    /// <summary>Parse the "path" entries out of libraryfolders.vdf.</summary>
    public static IEnumerable<string> ParseLibraryFolders(string vdfPath)
    {
        if (!File.Exists(vdfPath))
            yield break;

        foreach (Match match in Regex.Matches(File.ReadAllText(vdfPath), "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var value = match.Groups[1].Value;
            // VDF escapes backslashes as \\; collapse them back.
            yield return value.Replace("\\\\", "\\").Trim();
        }
    }

    /// <summary>Parse the "installdir" out of an appmanifest_&lt;appid&gt;.acf.</summary>
    public static string? ParseManifestInstallDir(string manifestPath)
    {
        var match = Regex.Match(File.ReadAllText(manifestPath), "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\").Trim() : null;
    }

    /// <summary>Parse the Steam build id out of an app manifest.</summary>
    public static string? ParseManifestBuildId(string manifestPath)
    {
        var match = Regex.Match(
            File.ReadAllText(manifestPath), "\"buildid\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static bool TryReadBuildId(
        string steamAppsPath,
        string gameFolderPath,
        int appId,
        out string? buildId)
    {
        buildId = null;
        var manifestPath = Path.Combine(steamAppsPath, $"appmanifest_{appId}.acf");
        if (!File.Exists(manifestPath))
            return false;

        var installDir = ParseManifestInstallDir(manifestPath);
        if (string.IsNullOrWhiteSpace(installDir))
            return false;

        var manifestGamePath = NormalizePath(Path.Combine(steamAppsPath, "common", installDir));
        if (!manifestGamePath.Equals(gameFolderPath, StringComparison.OrdinalIgnoreCase))
            return false;

        buildId = ParseManifestBuildId(manifestPath);
        return !string.IsNullOrWhiteSpace(buildId);
    }

    private static string SteamAppsPath(string libraryPath) =>
        Path.GetFileName(libraryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Equals("steamapps", StringComparison.OrdinalIgnoreCase)
            ? libraryPath
            : Path.Combine(libraryPath, "steamapps");

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
