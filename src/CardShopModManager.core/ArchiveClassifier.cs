namespace CardShopModManager.Core;

/// <summary>
/// Turns the files that came out of an archive into a concrete file-by-file
/// installation plan, guided by how the mod is structured. The rules are fixed
/// and documented so every archive produces a predictable plan.
///
/// Layout rules, checked in order:
///   1. Contains a BepInEx/ folder -> mirror it into the game's BepInEx/.
///   2. Loose .dll at the archive root -> whole mod goes to BepInEx/plugins/{Name}/.
///   3. Contains a patchers/ folder -> files go to BepInEx/patchers/.
///   4. Anything else -> mirror the archive root straight into the game root.
/// Documentation and OS-junk files are skipped, not installed.
/// </summary>
public sealed class ArchiveClassifier
{
    public enum LayoutKind
    {
        BepInExLayout,
        PluginFolder,
        Patcher,
        GameRoot,
        Empty
    }

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "readme.md", "readme.txt", "readme.rst",
        "license", "license.md", "license.txt", "license.rst",
        "notice", "notice.md", "notice.txt",
        "changelog", "changelog.md", "changelog.txt",
        "unknown", "unknown.md"
    };

    private static readonly HashSet<string> IgnoredFileNamesAnywhere = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbs.db", ".ds_store", "desktop.ini"
    };

    private const string MacOsJunkDirectory = "__macosx";

    public InstallPlan BuildPlan(
        ModEntry mod,
        IReadOnlyCollection<ExtractedSource> sources,
        IReadOnlyCollection<string>? rejected = null)
    {
        var kind = DetectLayout(sources);
        var files = new List<ArchiveContentEntry>();
        var skipped = new List<string>();

        foreach (var source in sources.OrderBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = source.RelativePath;

            if (IsIgnored(relativePath))
            {
                skipped.Add($"{relativePath} (ignored: documentation or OS junk)");
                continue;
            }

            var destinationRelativePath = MapToDestination(relativePath, mod.Name, kind);
            if (destinationRelativePath is null)
            {
                skipped.Add($"{relativePath} (not covered by layout {kind})");
                continue;
            }

            files.Add(new ArchiveContentEntry(source.AbsolutePath, relativePath, destinationRelativePath));
        }

        var layoutName = kind == LayoutKind.Empty
            ? "empty archive"
            : files.Count == 0
                ? "nothing installable (documentation/OS junk only)"
                : LayoutDisplayName(kind);

        return new InstallPlan(mod, layoutName, files, skipped, rejected?.ToList() ?? new List<string>());
    }

    private static LayoutKind DetectLayout(IReadOnlyCollection<ExtractedSource> sources)
    {
        if (sources.Count == 0)
            return LayoutKind.Empty;

        // Top-level folder names tell us which layout this mod uses.
        var topLevelNames = sources
            .Select(s => s.RelativePath.Split('/')[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (topLevelNames.Contains("BepInEx"))
            return LayoutKind.BepInExLayout;

        if (sources.Any(s =>
                !s.RelativePath.Contains('/') &&
                Path.GetExtension(s.RelativePath).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
            return LayoutKind.PluginFolder;

        if (topLevelNames.Contains("patchers"))
            return LayoutKind.Patcher;

        return LayoutKind.GameRoot;
    }

    private static string? MapToDestination(string relativePath, string modName, LayoutKind kind)
    {
        var segments = relativePath.Split('/');

        switch (kind)
        {
            case LayoutKind.BepInExLayout when segments[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase):
                return $"BepInEx/{string.Join('/', segments[1..])}";

            case LayoutKind.BepInExLayout:
                // Anything at the archive root alongside BepInEx/ (docs, etc.)
                // mirrors into the game root.
                return relativePath;

            case LayoutKind.PluginFolder:
                return $"BepInEx/plugins/{modName}/{relativePath}";

            case LayoutKind.Patcher when segments[0].Equals("patchers", StringComparison.OrdinalIgnoreCase):
                return $"BepInEx/patchers/{string.Join('/', segments[1..])}";

            case LayoutKind.Patcher:
                return $"BepInEx/plugins/{modName}/{relativePath}";

            case LayoutKind.GameRoot:
                return relativePath;

            default:
                return null;
        }
    }

    private static bool IsIgnored(string relativePath)
    {
        if (relativePath.StartsWith(MacOsJunkDirectory + "/", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = relativePath.Split('/').Last();
        return IgnoredFileNames.Contains(fileName) || IgnoredFileNamesAnywhere.Contains(fileName);
    }

    private static string LayoutDisplayName(LayoutKind kind) => kind switch
    {
        LayoutKind.BepInExLayout => "BepInEx layout (mirrors the game's BepInEx folder)",
        LayoutKind.PluginFolder => "loose plugin folder (goes to BepInEx/plugins/<mod name>)",
        LayoutKind.Patcher => "patcher layout (goes to BepInEx/patchers)",
        LayoutKind.GameRoot => "game root files (mirrors into the game folder root)",
        _ => "empty archive"
    };
}