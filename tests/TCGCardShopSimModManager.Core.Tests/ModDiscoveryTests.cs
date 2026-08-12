using System.Security.Cryptography;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ModDiscoveryTests : IDisposable
{
    private readonly string _root;
    private readonly string _gameFolder;
    private readonly string _sourceDir;
    private readonly ModInstaller _installer;

    public ModDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "discovery-tests-" + Guid.NewGuid().ToString("N"));
        _gameFolder = Path.Combine(_root, "game");
        _sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(_gameFolder);
        Directory.CreateDirectory(_sourceDir);
        _installer = new ModInstaller(_gameFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Discover_EmptyGameFolder_ReturnsNoMods()
    {
        Assert.Empty(ModDiscovery.Discover(_gameFolder));
    }

    [Fact]
    public void Discover_HandInstalledMod_IsUnknown()
    {
        // A mod placed by hand (no journal) must be reported, not hidden.
        var folder = Path.Combine(_gameFolder, "BepInEx", "plugins", "Hand Mod");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Hand.dll"), "bytes");

        var mod = Assert.Single(ModDiscovery.Discover(_gameFolder));
        Assert.Equal("Hand Mod", mod.ModName);
        Assert.Equal(ModInventoryState.Unknown, mod.State);
    }

    [Fact]
    public void Discover_InstalledMod_IsInstalled()
    {
        Install("Example Mod", "ExampleMod.dll");

        var mod = Assert.Single(ModDiscovery.Discover(_gameFolder));
        Assert.Equal(ModInventoryState.Installed, mod.State);
    }

    [Fact]
    public void Discover_ModifiedFile_IsModified()
    {
        Install("Example Mod", "ExampleMod.dll");

        var installedFile = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        File.WriteAllText(installedFile, "tampered");

        var mod = Assert.Single(ModDiscovery.Discover(_gameFolder));
        Assert.Equal(ModInventoryState.Modified, mod.State);
    }

    [Fact]
    public void Disable_MovesFilesToDisabledAndReportsDisabled()
    {
        Install("Example Mod", "ExampleMod.dll");
        var active = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod");
        var disabledFile = Path.Combine(_gameFolder, "BepInEx", "disabled", "Example Mod", "ExampleMod.dll");

        var result = _installer.Disable("Example Mod");

        Assert.True(result.Success);
        Assert.True(File.Exists(disabledFile));
        Assert.False(Directory.Exists(active)); // emptied folder pruned

        var mod = Assert.Single(ModDiscovery.Discover(_gameFolder));
        Assert.Equal(ModInventoryState.Disabled, mod.State);
    }

    [Fact]
    public void Enable_MovesFilesBackAndReportsInstalled()
    {
        Install("Example Mod", "ExampleMod.dll");
        _installer.Disable("Example Mod");

        var result = _installer.Enable("Example Mod");

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll")));

        var mod = Assert.Single(ModDiscovery.Discover(_gameFolder));
        Assert.Equal(ModInventoryState.Installed, mod.State);
    }

    [Fact]
    public void Disable_LeavesModifiedFileInPlaceWithWarning()
    {
        Install("Example Mod", "ExampleMod.dll");
        var installedFile = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        File.WriteAllText(installedFile, "tampered");

        var result = _installer.Disable("Example Mod");

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("Modified"));
        Assert.True(File.Exists(installedFile));
    }

    [Fact]
    public void Disable_UnknownMod_Fails()
    {
        var result = _installer.Disable("Never Installed");
        Assert.False(result.Success);
        Assert.Contains("No journal entry", result.Error);
    }

    // --- helpers -----------------------------------------------------------

    private void Install(string modName, string fileName)
    {
        var sourcePath = Path.Combine(_sourceDir, fileName);
        File.WriteAllText(sourcePath, "dll-bytes");

        var mod = new ModEntry("example-mod", modName, null, fileName,
            ComputeSha256(sourcePath), "BepInExPlugin", new List<string>(), new List<string>());

        var installed = _installer.Install(mod, _sourceDir);
        Assert.True(installed.Success, installed.Error);
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
}