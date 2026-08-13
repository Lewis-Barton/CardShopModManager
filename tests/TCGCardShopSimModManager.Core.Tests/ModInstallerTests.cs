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
        Assert.Equal(mod.Id, entry.ModId);
        Assert.Equal(mod.Version, entry.Version);
        Assert.Equal(mod.Sha256, entry.ArchiveSha256);
    }

    [Fact]
    public void Install_UpdateReplacesAddsAndRemovesOwnedFiles()
    {
        var firstZip = CreateZip(
            ("ExampleMod.dll", "version-one"),
            ("old.cfg", "old-setting"));
        var first = AddZip("pack.zip", firstZip) with
        {
            Id = "stable-id",
            Name = "Example Mod",
            Version = "1.0.0"
        };
        var firstResult = _installer.Install(first, _sourceDir);
        Assert.True(firstResult.Success, firstResult.Error);

        var secondZip = CreateZip(
            ("ExampleMod.dll", "version-two"),
            ("new.cfg", "new-setting"));
        var second = AddZip("pack.zip", secondZip) with
        {
            Id = "stable-id",
            Name = "Example Mod Renamed",
            Version = "2.0.0"
        };

        var result = _installer.Install(second, _sourceDir);

        Assert.True(result.Success, result.Error);
        var pluginRoot = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod Renamed");
        Assert.Equal("version-two", File.ReadAllText(Path.Combine(pluginRoot, "ExampleMod.dll")));
        Assert.Equal("new-setting", File.ReadAllText(Path.Combine(pluginRoot, "new.cfg")));
        Assert.False(File.Exists(Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "old.cfg")));
        var entry = Assert.Single(new JournalStore(_gameFolder).Load());
        Assert.Equal("stable-id", entry.ModId);
        Assert.Equal("2.0.0", entry.Version);
        Assert.Equal(second.Sha256, entry.ArchiveSha256);
    }

    [Fact]
    public void Install_UpdateRefusesToReplaceModifiedOwnedFile()
    {
        var first = AddLooseFile("ExampleMod.dll", "version-one") with
        {
            Id = "stable-id",
            Version = "1.0.0"
        };
        Assert.True(_installer.Install(first, _sourceDir).Success);
        var installed = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        File.WriteAllText(installed, "user-change");

        var second = AddLooseFile("ExampleMod.dll", "version-two") with
        {
            Id = "stable-id",
            Version = "2.0.0"
        };
        var result = _installer.Install(second, _sourceDir);

        Assert.False(result.Success);
        Assert.Contains("modified", result.Error);
        Assert.Equal("user-change", File.ReadAllText(installed));
        var entry = Assert.Single(new JournalStore(_gameFolder).Load());
        Assert.Equal("1.0.0", entry.Version);
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
    public void Install_RejectsDestinationOutsideGameFolder_WhenValidationIsBypassed()
    {
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes") with { Name = "../../../escaped" };
        var escaped = Path.Combine(_testRoot, "escaped", "ExampleMod.dll");

        var result = _installer.Install(mod, _sourceDir);

        Assert.False(result.Success);
        Assert.Contains("escapes the game folder", result.Error);
        Assert.False(File.Exists(escaped));
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

    [Fact]
    public void Disable_FrameworkMod_ReportsNonSuccess() // BUG-011
    {
        // A mod whose files live under BepInEx/core (the framework) is not one we
        // toggle, so disabling it must report failure, not a silent success.
        var zipPath = CreateZip(("BepInEx/core/SomeFramework/framework.dll", "dll-bytes"));
        var mod = AddZip("fw.zip", zipPath);
        Assert.True(_installer.Install(mod, _sourceDir).Success);

        var result = _installer.Disable(mod.Name);

        Assert.False(result.Success);
        Assert.Contains("not a managed", result.Error ?? "");
    }

    [Fact]
    public void Disable_AlreadyDisabledMod_ReportsAlreadyDisabled() // BUG-018
    {
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes");
        Assert.True(_installer.Install(mod, _sourceDir).Success);
        Assert.True(_installer.Disable(mod.Name).Success);

        var result = _installer.Disable(mod.Name);

        Assert.True(result.Success);
        Assert.Equal("Already disabled: Example Mod", result.Message);
    }

    [Fact]
    public void Disable_ReinstallThenDisable_DoesNotThrow() // BUG-016
    {
        // disable -> reinstall (journaled, active copy regenerated) -> disable again
        // used to crash with "file already exists" on the occupied disabled path.
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes");
        Assert.True(_installer.Install(mod, _sourceDir).Success);
        Assert.True(_installer.Disable(mod.Name).Success);
        Assert.True(_installer.Install(mod, _sourceDir).Success);

        var result = _installer.Disable(mod.Name);

        Assert.True(result.Success);
    }

    [Fact]
    public void Uninstall_MissingGameFolder_ReportsGameFolderNotFound() // BUG-040
    {
        var missing = new ModInstaller(
            Path.Combine(_testRoot, "does-not-exist"),
            Path.Combine(_testRoot, "disabled"));

        var result = missing.Uninstall("AnyMod");

        Assert.False(result.Success);
        Assert.Contains("Game folder not found", result.Error ?? "");
    }

    [Fact]
    public void Uninstall_KeepsJournalEntryWhenFileModified() // BUG-014
    {
        var mod = AddLooseFile("ExampleMod.dll", "dll-bytes");
        Assert.True(_installer.Install(mod, _sourceDir).Success);
        var installed = Path.Combine(_gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        File.WriteAllText(installed, "tampered");

        var result = _installer.Uninstall(mod.Name);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("modified"));
        Assert.True(File.Exists(installed));
        // The journal entry must survive so the stranded mod stays tracked.
        Assert.Contains(new JournalStore(_gameFolder).Load(), e => e.ModName == mod.Name);
    }

    [Fact]
    public void Uninstall_LastModOfPack_ClearsPackJournal() // BUG-005
    {
        var packStore = new ModpackJournalStore(_gameFolder);
        packStore.Record("shared-pack", "1.0", "Shared Pack");

        var modA = new ModEntry("mod-a", "ModA", null, "a.dll", "", "BepInExPlugin", new(), new(), PackId: "shared-pack");
        var modB = new ModEntry("mod-b", "ModB", null, "b.dll", "", "BepInExPlugin", new(), new(), PackId: "shared-pack");
        File.WriteAllText(Path.Combine(_sourceDir, "a.dll"), "bytes-a");
        File.WriteAllText(Path.Combine(_sourceDir, "b.dll"), "bytes-b");
        modA = modA with { Sha256 = ComputeSha256(Path.Combine(_sourceDir, "a.dll")) };
        modB = modB with { Sha256 = ComputeSha256(Path.Combine(_sourceDir, "b.dll")) };
        Assert.True(_installer.Install(modA, _sourceDir).Success);
        Assert.True(_installer.Install(modB, _sourceDir).Success);

        Assert.Contains(packStore.Load(), p => p.PackId == "shared-pack");

        _installer.Uninstall("ModA");
        // pack should remain while another mod of it is still installed
        Assert.Contains(packStore.Load(), p => p.PackId == "shared-pack");

        _installer.Uninstall("ModB");
        Assert.DoesNotContain(packStore.Load(), p => p.PackId == "shared-pack");
    }

    [Fact]
    public void JournalStore_ToleratesCorruptFile() // BUG-015
    {
        File.WriteAllText(Path.Combine(_gameFolder, "cardshopmodmanager.journal.json"), "{ not valid json");
        var store = new JournalStore(_gameFolder);

        var entries = store.Load();

        Assert.Empty(entries);
        Assert.True(File.Exists(Path.Combine(_gameFolder, "cardshopmodmanager.journal.json.corrupt")));
    }

    [Fact]
    public void JournalStore_LoadsLegacyEntryWithoutIdentityFields()
    {
        var journalPath = Path.Combine(_gameFolder, "cardshopmodmanager.journal.json");
        File.WriteAllText(journalPath, """
            [
              {
                "modName": "Legacy Mod",
                "installedAt": "2026-08-13T12:00:00Z",
                "files": []
              }
            ]
            """);

        var entry = Assert.Single(new JournalStore(_gameFolder).Load());

        Assert.Equal("Legacy Mod", entry.ModName);
        Assert.Null(entry.ModId);
        Assert.Null(entry.Version);
        Assert.Null(entry.ArchiveSha256);
    }

    [Fact]
    public void ModpackJournalStore_ToleratesCorruptFile() // BUG-004
    {
        File.WriteAllText(Path.Combine(_gameFolder, "cardshopmodmanager.modpacks.json"), "{ not valid json");
        var store = new ModpackJournalStore(_gameFolder);

        var entries = store.Load();

        Assert.Empty(entries);
        Assert.True(File.Exists(Path.Combine(_gameFolder, "cardshopmodmanager.modpacks.json.corrupt")));
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
