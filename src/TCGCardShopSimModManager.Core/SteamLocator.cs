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
            var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifestPath))
                continue;

            var installDir = ParseManifestInstallDir(manifestPath);
            if (string.IsNullOrWhiteSpace(installDir))
                continue;

            var gamePath = Path.Combine(library, "steamapps", "common", installDir);
            if (Directory.Exists(gamePath))
                return gamePath;
        }

        return null;
    }

    public string? ReadRegistrySteamPath()
    {
        // The registry read is Windows-only; Steam detection simply cannot
        // happen elsewhere.
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
}