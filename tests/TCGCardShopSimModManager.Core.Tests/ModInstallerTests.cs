using System.IO.Compression;
using System.Security.Cryptography;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ModInstallerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _gameFolder;
    private readonly string _sourceDir;
    private readonly ModInstaller _installer;

    public ModInstallerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "install-tests-" + Guid.NewGuid().ToString("N"));
        _gameFolder = Path.Combine(_testRoot, "game");
        _sourceDir = Path.Combine(_testRoot, "source");
        Directory.CreateDirectory(_gameFolder);
        Directory.CreateDirectory(_sourceDir);
        _installer = new ModInstaller(_gameFolder, Path.Combine(_testRoot, "disabled"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Fact]
    public void Install_LooseFile_GoesToPluginFolderAndJournals()
    {
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes");

        var result = _installer.Install(mod, _sourceDir);

        Assert.True(result.Success);
        var installed = Assert.Single(result.InstalledPaths!);
        Assert.EndsWith(Path.Combine("BepInEx", "plugins", "Example Mod", "ExampleMod.dll"), installed);
        Assert.True(File.Exists(installed));

        var entry = Assert.Single(new JournalStore(_gameFolder).Load(), e => e.ModName == mod.Name);
        var file = Assert.Single(entry.Files);
        Assert.Equal(installed, file.Path);
    }

    [Fact]
    public void Install_ZipWithBepInExLayout_LandsInsideAndJournalsEveryFile()
    {
        var zipPath = CreateZip(
            ("BepInEx/plugins/RealMod.dll", "dll-bytes"),
            ("BepInEx/config/settings.cfg", "cfg-bytes"),
            ("README.md", "docs"));
        var mod = AddZip("pack.zip", zipPath);
        var expectedFiles = new[]
        {
            Path.Combine(_gameFolder, "BepInEx", "plugins", "RealMod.dll"),
            Path.Combine(_gameFolder, "BepInEx", "config", "settings.cfg")
        };

        var result = _installer.Install(mod, _sourceDir);

        Assert.True(result.Success);
        Assert.Equal(2, result.InstalledPaths!.Count);
        Assert.True(expectedFiles.All(File.Exists));
        Assert.False(File.Exists(Path.Combine(_gameFolder, "README.md"))); // docs skipped

        var entry = Assert.Single(new JournalStore(_gameFolder).Load(), e => e.ModName == mod.Name);
        Assert.Equal(2, entry.Files.Count);
        Assert.All(entry.Files, f => Assert.True(File.Exists(f.Path)));
    }

    [Fact]
    public void Install_RefusesToOverwriteExistingFile()
    {
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes");
        var destination = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "pre-existing");

        var result = _installer.Install(mod, _sourceDir);

        Assert.False(result.Success);
        Assert.Contains("refusing to overwrite", result.Error);
        Assert.Equal("pre-existing", File.ReadAllText(destination));
        Assert.Empty(new JournalStore(_gameFolder).Load());
    }

    [Fact]
    public void Install_WorksWithSpacesInGamePath()
    {
        // The real-world matrix: a library path containing spaces must behave
        // exactly like any other path.
        var gameFolderWithSpaces = Path.Combine(_testRoot, "my game folder");
        Directory.CreateDirectory(gameFolderWithSpaces);

        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes");

        var result = new ModInstaller(gameFolderWithSpaces, Path.Combine(_testRoot, "disabled")).Install(mod, _sourceDir);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(Path.Combine(gameFolderWithSpaces, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll")));
    }

    [Fact]
    public void Install_RejectsArchiveHashMismatch()
    {
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes") with { Sha256 = new string('0', 64) };

        var result = _installer.Install(mod, _sourceDir);

        Assert.False(result.Success);
        Assert.Contains("Hash mismatch", result.Error);
        Assert.Empty(new JournalStore(_gameFolder).Load());
    }

    [Fact]
    public void Install_DocsOnlyArchive_Refused()
    {
        var zipPath = CreateZip(("README.md", "docs"));
        var mod = AddZip("docs.zip", zipPath);

        var result = _installer.Install(mod, _sourceDir);

        Assert.False(result.Success);
        Assert.Contains("nothing to install", result.Error);
        Assert.Empty(new JournalStore(_gameFolder).Load());
    }

    [Fact]
    public void Install_RejectedExecutableArchive_Refused()
    {
        var zipPath = CreateZip(("install.bat", "del /q game.exe"));
        var mod = AddZip("attack.zip", zipPath);

        var result = _installer.Install(mod, _sourceDir);

        Assert.False(result.Success);
        Assert.Empty(new JournalStore(_gameFolder).Load());
    }

    [Fact]
    public void Install_SurfacesRejectedExecutable_WhileInstallingRest()
    {
        // A mod that bundles a malicious .exe alongside a good plugin: the .exe
        // must be refused, the plugin installed, and the refusal surfaced.
        var zipPath = CreateZip(
            ("BepInEx/plugins/Mod/mod.dll", "dll-bytes"),
            ("evil.exe", "trample"));
        var mod = AddZip("mixed.zip", zipPath);

        var result = _installer.Install(mod, _sourceDir);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.RejectedEntries);
        Assert.Contains(result.RejectedEntries, r => r.Contains("evil.exe"));
        Assert.True(File.Exists(Path.Combine(_gameFolder, "BepInEx", "plugins", "Mod", "mod.dll")));
        Assert.False(File.Exists(Path.Combine(_gameFolder, "evil.exe")));
    }

    [Fact]
    public void CreatePlan_ThrowsOnTruncatedArchive()
    {
        // An archive that blows the size cap must fail loudly, not install a
        // partial copy and report success.
        var zipPath = CreateZip(
            ("a.txt", new string('x', 100)),
            ("b.txt", new string('x', 100)),
            ("c.txt", new string('x', 100)));
        var mod = AddZip("big.zip", zipPath);

        var settings = ArchiveProtectionSettings.Default with { MaxTotalBytes = 150 };
        var workDir = Path.Combine(_testRoot, "plan");

        Assert.Throws<InvalidDataException>(() =>
            _installer.CreatePlan(mod, _sourceDir, workDir, settings));
    }

    [Fact]
    public void Uninstall_RemovesAllInstalledFilesAndJournalEntry()
    {
        var zipPath = CreateZip(
            ("BepInEx/plugins/RealMod.dll", "dll-bytes"),
            ("BepInEx/config/settings.cfg", "cfg-bytes"));
        var mod = AddZip("pack.zip", zipPath);
        Assert.True(_installer.Install(mod, _sourceDir).Success);

        var result = _installer.Uninstall(mod.Name);

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(Path.Combine(_gameFolder, "BepInEx", "plugins", "RealMod.dll")));
        Assert.False(File.Exists(Path.Combine(_gameFolder, "BepInEx", "config", "settings.cfg")));
        Assert.DoesNotContain(new JournalStore(_gameFolder).Load(), e => e.ModName == mod.Name);
    }

    [Fact]
    public void Uninstall_WarnsButKeepsFile_WhenFileWasModified()
    {
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes");
        Assert.True(_installer.Install(mod, _sourceDir).Success);

        var installed = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        File.WriteAllText(installed, "tampered-with-a-different-length");
        Assert.Equal("tampered-with-a-different-length", File.ReadAllText(installed)); // prove the tamper landed before uninstalling

        var result = _installer.Uninstall(mod.Name);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("modified"));
        Assert.True(File.Exists(installed));
    }

    [Fact]
    public void Uninstall_ReportsError_WhenModNotInJournal()
    {
        var result = _installer.Uninstall("Never Installed");
        Assert.False(result.Success);
        Assert.Contains("No journal entry", result.Error);
    }

    // --- helpers -----------------------------------------------------------

    private ModEntry AddLooseFile(string fileName, string content)
    {
        var path = Path.Combine(_sourceDir, fileName);
        File.WriteAllText(path, content);
        return new ModEntry("example-mod", "Example Mod", null, fileName, ComputeSha256(path),
            "BepInExPlugin", new List<string>(), new List<string>());
    }

    private ModEntry AddZip(string archiveName, string zipPath)
    {
        File.Copy(zipPath, Path.Combine(_sourceDir, archiveName), overwrite: true);
        return new ModEntry("zipped-mod", "Zipped Mod", null, archiveName,
            ComputeSha256(Path.Combine(_sourceDir, archiveName)),
            "BepInExPlugin", new List<string>(), new List<string>());
    }

    private static string CreateZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), "install-tests-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return path;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}