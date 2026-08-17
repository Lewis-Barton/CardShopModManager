using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class SteamLocatorTests : IDisposable
{
    private readonly string _root;
    private readonly string _steamRoot;
    private readonly string _library2;
    private readonly string _gameCommon;

    public SteamLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "steam-tests-" + Guid.NewGuid().ToString("N"));
        _steamRoot = Path.Combine(_root, "Steam");
        _library2 = Path.Combine(_root, "Library2");
        _gameCommon = Path.Combine(_library2, "steamapps", "common", "TCG Card Shop Simulator");

        Directory.CreateDirectory(_steamRoot);
        Directory.CreateDirectory(_gameCommon);
        File.WriteAllBytes(Path.Combine(_gameCommon, "Card Shop Simulator.exe"), new byte[] { 1 });

        var primary = Path.Combine(_steamRoot, "steamapps");
        Directory.CreateDirectory(primary);

        var vdf =
            "\"libraryfolders\"\n" +
            "{\n" +
            "  \"0\"\n  {\n    \"path\" \"" + EscapeVdf(_steamRoot) + "\"\n  }\n" +
            "  \"1\"\n  {\n    \"path\" \"" + EscapeVdf(_library2) + "\"\n  }\n" +
            "}\n";
        File.WriteAllText(Path.Combine(primary, "libraryfolders.vdf"), vdf);

        var manifest =
            "\"AppState\"\n" +
            "{\n" +
            "  \"appid\" \"3070070\"\n" +
            "  \"name\" \"TCG Card Shop Simulator\"\n" +
            "  \"installdir\" \"TCG Card Shop Simulator\"\n" +
            "  \"buildid\" \"19024567\"\n" +
            "}\n";
        File.WriteAllText(Path.Combine(_library2, "steamapps", "appmanifest_3070070.acf"), manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindGameInstallPath_ReturnsInstalledGameFolder()
    {
        var locator = new SteamLocator(_steamRoot);

        var path = locator.FindGameInstallPath(SteamLocator.GameAppId);

        Assert.Equal(_gameCommon, path);
    }

    [Fact]
    public void FindGameInstallPath_ReturnsNull_WhenGameNotInstalled()
    {
        var locator = new SteamLocator(_steamRoot);

        var path = locator.FindGameInstallPath(99999);

        Assert.Null(path);
    }

    [Fact]
    public void FindGameInstallPath_ReturnsNull_WhenSteamRootMissing()
    {
        var locator = new SteamLocator(Path.Combine(_root, "NoSteamHere"));

        var path = locator.FindGameInstallPath(SteamLocator.GameAppId);

        Assert.Null(path);
    }

    [Fact]
    public void ParseLibraryFolders_IncludesPrimaryAndAllVdfPaths()
    {
        var libraries = SteamLocator.Libraries(_steamRoot);

        Assert.Contains(Path.Combine(_steamRoot, "steamapps"), libraries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(_library2, libraries, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseManifestInstallDir_ReadsTheInstallDirectory()
    {
        var manifestPath = Path.Combine(_library2, "steamapps", "appmanifest_3070070.acf");

        var installDir = SteamLocator.ParseManifestInstallDir(manifestPath);

        Assert.Equal("TCG Card Shop Simulator", installDir);
    }

    [Fact]
    public void FindGameBuildId_ReadsBuildForSelectedInstallFolder()
    {
        var buildId = new SteamLocator(_steamRoot).FindGameBuildId(
            _gameCommon, SteamLocator.GameAppId);

        Assert.Equal("19024567", buildId);
    }

    [Fact]
    public void ParseManifestBuildId_ReadsSteamBuildId()
    {
        var manifestPath = Path.Combine(_library2, "steamapps", "appmanifest_3070070.acf");

        Assert.Equal("19024567", SteamLocator.ParseManifestBuildId(manifestPath));
    }

    private static string EscapeVdf(string path) => path.Replace("\\", "\\\\");
}
