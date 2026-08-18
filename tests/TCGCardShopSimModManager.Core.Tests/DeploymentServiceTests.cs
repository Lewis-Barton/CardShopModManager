using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

/// <summary>
/// Integration-level tests for the single orchestration path that both the CLI
/// and the desktop app call.
/// </summary>
public sealed class DeploymentServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceDir;

    public DeploymentServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "deployment-tests-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Validate_InvalidManifest_ReportsEveryReason()
    {
        var manifestPath = WriteManifest(new[]
        {
            MakeModJson("mod-a", dependencies: new[] { "mod-b" }),
            MakeModJson("mod-b", dependencies: new[] { "mod-a" }),
            MakeModJson("mod-c", conflicts: new[] { "mod-d" }),
            MakeModJson("mod-d"),
            MakeModJson("mod-e", dependencies: new[] { "ghost-library" })
        });

        var report = new DeploymentService().Validate(manifestPath, null);

        Assert.False(report.Success);
        var text = string.Join('\n', report.Lines);
        Assert.Contains("Circular dependency", text);
        Assert.Contains("conflict", text);
        Assert.Contains("ghost-library", text);
    }

    [Fact]
    public void Validate_ValidManifest_ShowsInstallOrder()
    {
        var manifestPath = WriteManifest(new[]
        {
            MakeModJson("cool-plugin", archive: "plugin.zip", dependencies: new[] { "core-library" }),
            MakeModJson("core-library", archive: "library.zip")
        });

        var report = new DeploymentService().Validate(manifestPath, null);

        Assert.True(report.Success);
        var text = string.Join('\n', report.Lines);
        Assert.True(text.IndexOf("core-library", StringComparison.OrdinalIgnoreCase)
                    < text.IndexOf("cool-plugin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Install_ZippedMod_LandsFilesAndJournals()
    {
        WriteZip(Path.Combine(_sourceDir, "library.zip"), ("BepInEx/plugins/Lib.dll", "lib-bytes"));
        var manifestPath = WriteManifest(new[]
        {
            MakeModJson("core-library", archive: "library.zip", sha: ShaOf(Path.Combine(_sourceDir, "library.zip")))
        });
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);

        var report = new DeploymentService().Install(manifestPath, _sourceDir, gameFolder);

        Assert.True(report.Success);
        Assert.True(File.Exists(Path.Combine(gameFolder, "BepInEx", "plugins", "Lib.dll")));
        Assert.Single(new JournalStore(gameFolder).Load());
    }

    [Fact]
    public void Install_ReportsFailureWhenAModInstallsNothing_Bug017()
    {
        // A mod whose only content is a refused DLL yields zero installable files:
        // it passes validation and pre-flight planning, then fails inside the
        // install loop. The whole command must fail and undo the earlier mod
        // rather than leave a partial deployment behind.
        WriteZip(Path.Combine(_sourceDir, "good.zip"), ("BepInEx/plugins/Good.dll", "g"));
        WriteZip(Path.Combine(_sourceDir, "empty.zip"), ("winhttp.dll", "x"));
        var manifestPath = WriteManifest(new[]
        {
            MakeModJson("good-mod", archive: "good.zip", sha: ShaOf(Path.Combine(_sourceDir, "good.zip"))),
            MakeModJson("empty-mod", archive: "empty.zip", sha: ShaOf(Path.Combine(_sourceDir, "empty.zip")))
        });
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);

        var report = new DeploymentService().Install(manifestPath, _sourceDir, gameFolder);

        Assert.False(report.Success);
        Assert.Contains(report.Lines, l => l.Contains("Failed to install empty-mod"));
        Assert.False(File.Exists(Path.Combine(gameFolder, "BepInEx", "plugins", "Good.dll")));
        Assert.Empty(new JournalStore(gameFolder).Load());
        Assert.Contains(report.Lines, line => line.Contains("rollback completed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Install_RefusesConflictWithInstalledMod_Bug019()
    {
        // BUG-019: a second mod that would overwrite a file already owned by an
        // installed mod must be refused at pre-flight, not mid-install.
        WriteZip(Path.Combine(_sourceDir, "a.zip"), ("BepInEx/plugins/Shared/common.dll", "a"));
        WriteZip(Path.Combine(_sourceDir, "b.zip"), ("BepInEx/plugins/Shared/common.dll", "b"));
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);

        var manifestA = WriteManifest(new[] { MakeModJson("mod-a", archive: "a.zip", sha: ShaOf(Path.Combine(_sourceDir, "a.zip"))) },
            "manifestA.json");
        Assert.True(new DeploymentService().Install(manifestA, _sourceDir, gameFolder).Success);
        Assert.True(File.Exists(Path.Combine(gameFolder, "BepInEx", "plugins", "Shared", "common.dll")));

        var manifestB = WriteManifest(new[] { MakeModJson("mod-b", archive: "b.zip", sha: ShaOf(Path.Combine(_sourceDir, "b.zip"))) },
            "manifestB.json");
        var report = new DeploymentService().Install(manifestB, _sourceDir, gameFolder);

        Assert.False(report.Success);
        Assert.Contains(report.Lines, l => l.Contains("File conflicts detected") || l.Contains("common.dll"));
    }

    [Fact]
    public void Install_ManifestExclusionAssignsOneOwnerForBundledFile()
    {
        WriteZip(Path.Combine(_sourceDir, "a.zip"),
            ("BepInEx/plugins/Shared/common.dll", "shared"),
            ("BepInEx/plugins/A/a.dll", "a"));
        WriteZip(Path.Combine(_sourceDir, "b.zip"),
            ("BepInEx/plugins/Shared/common.dll", "shared"),
            ("BepInEx/plugins/B/b.dll", "b"));
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);
        var first = MakeModJson("mod-a", archive: "a.zip", sha: ShaOf(Path.Combine(_sourceDir, "a.zip")));
        var second = MakeModJson("mod-b", archive: "b.zip", sha: ShaOf(Path.Combine(_sourceDir, "b.zip")),
            excludedArchivePaths: ["BepInEx/plugins/Shared/common.dll"]);
        var manifest = WriteManifest([first, second]);

        var report = new DeploymentService().Install(manifest, _sourceDir, gameFolder);

        Assert.True(report.Success, string.Join("\n", report.Lines));
        Assert.Equal("shared", File.ReadAllText(Path.Combine(
            gameFolder, "BepInEx", "plugins", "Shared", "common.dll")));
        var entries = new JournalStore(gameFolder).Load();
        Assert.Contains(entries.Single(entry => entry.ModId == "mod-a").Files,
            file => file.Path.EndsWith("common.dll"));
        Assert.DoesNotContain(entries.Single(entry => entry.ModId == "mod-b").Files,
            file => file.Path.EndsWith("common.dll"));
    }

    [Fact]
    public void Install_LongConflictReportIsCondensed()
    {
        var shared = Enumerable.Range(1, 25)
            .Select(number => ($"BepInEx/plugins/Shared/{number}.txt", number.ToString()))
            .ToArray();
        WriteZip(Path.Combine(_sourceDir, "a.zip"), shared);
        WriteZip(Path.Combine(_sourceDir, "b.zip"), shared);
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);
        var manifest = WriteManifest(
        [
            MakeModJson("mod-a", archive: "a.zip", sha: ShaOf(Path.Combine(_sourceDir, "a.zip"))),
            MakeModJson("mod-b", archive: "b.zip", sha: ShaOf(Path.Combine(_sourceDir, "b.zip")))
        ]);

        var report = new DeploymentService().Install(manifest, _sourceDir, gameFolder);

        Assert.False(report.Success);
        Assert.Contains(report.Lines, line => line == "  ... and 5 more conflict(s).");
        Assert.Equal(20, report.Lines.Count(line => line.Contains("is claimed by")));
    }

    [Fact]
    public void Validate_EnforcesBepInExFirst_Bug020()
    {
        // BUG-020: the local validate path must order BepInEx before plugins even
        // when the manifest lists it last and a plugin does not depend on it.
        var manifestPath = WriteManifest(new[]
        {
            MakeModJson("plugin1", archive: "p1.zip", dependencies: new[] { "core-library" }),
            MakeModJson("core-library", archive: "l.zip"),
            MakeModJson("bepinex", archive: "b.zip", installType: "BepInEx")
        });

        var report = new DeploymentService().Validate(manifestPath, null);

        Assert.True(report.Success);
        var text = string.Join('\n', report.Lines);
        var bepinexIdx = text.IndexOf("bepinex", StringComparison.OrdinalIgnoreCase);
        var pluginIdx = text.IndexOf("plugin1", StringComparison.OrdinalIgnoreCase);
        Assert.True(bepinexIdx >= 0 && pluginIdx >= 0, text);
        Assert.True(bepinexIdx < pluginIdx, $"BepInEx must be ordered before plugins.\n{text}");
    }

    [Fact]
    public void Validate_MalformedManifest_ReturnsFriendlyJsonError_Bug026()
    {
        // BUG-026: a malformed manifest must surface a friendly "not valid JSON"
        // message, not the raw serializer exception through the top-level handler.
        var manifestPath = Path.Combine(_root, "bad.json");
        File.WriteAllText(manifestPath, "{ \"manifestVersion\": 1, \"name\": \"oops\", ");

        var report = new DeploymentService().Validate(manifestPath, null);

        Assert.False(report.Success);
        Assert.Contains(report.Lines, l => l.Contains("not valid JSON"));
    }

    [Fact]
    public void Install_MalformedManifest_ReturnsFriendlyJsonError_Bug026()
    {
        var manifestPath = Path.Combine(_root, "bad.json");
        File.WriteAllText(manifestPath, "not json at all");
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);

        var report = new DeploymentService().Install(manifestPath, _sourceDir, gameFolder);

        Assert.False(report.Success);
        Assert.Contains(report.Lines, l => l.Contains("not valid JSON"));
    }

    [Fact]
    public void Install_NewerArchiveUpdatesExistingMod()
    {
        var archive = Path.Combine(_sourceDir, "plugin.zip");
        WriteZip(archive, ("ExampleMod.dll", "version-one"));
        var manifestPath = WriteManifest(new[]
        {
            MakeModJson("example-mod", archive: "plugin.zip", sha: ShaOf(archive))
        });
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);
        var service = new DeploymentService();
        Assert.True(service.Install(manifestPath, _sourceDir, gameFolder).Success);

        WriteZip(archive, ("ExampleMod.dll", "version-two"));
        manifestPath = WriteManifest(new[]
        {
            MakeModJson("example-mod", archive: "plugin.zip", sha: ShaOf(archive))
        });

        var report = service.Install(manifestPath, _sourceDir, gameFolder);

        Assert.True(report.Success, string.Join("\n", report.Lines));
        Assert.Contains(report.Lines, line => line.StartsWith("Updated example-mod"));
        var installed = Path.Combine(gameFolder, "BepInEx", "plugins", "example-mod", "ExampleMod.dll");
        Assert.Equal("version-two", File.ReadAllText(installed));
    }

    [Fact]
    public void Install_PreflightBlocksAllUpdatesWhenOneManagedFileWasModified()
    {
        var firstArchive = Path.Combine(_sourceDir, "first.zip");
        var secondArchive = Path.Combine(_sourceDir, "second.zip");
        WriteZip(firstArchive, ("First.dll", "first-v1"));
        WriteZip(secondArchive, ("Second.dll", "second-v1"));
        var manifestPath = WriteManifest(new[]
        {
            MakeModJson("first", archive: "first.zip", sha: ShaOf(firstArchive)),
            MakeModJson("second", archive: "second.zip", sha: ShaOf(secondArchive))
        });
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);
        var service = new DeploymentService();
        Assert.True(service.Install(manifestPath, _sourceDir, gameFolder).Success);

        var firstInstalled = Path.Combine(gameFolder, "BepInEx", "plugins", "first", "First.dll");
        var secondInstalled = Path.Combine(gameFolder, "BepInEx", "plugins", "second", "Second.dll");
        File.WriteAllText(secondInstalled, "user-change");
        WriteZip(firstArchive, ("First.dll", "first-v2"));
        WriteZip(secondArchive, ("Second.dll", "second-v2"));
        manifestPath = WriteManifest(new[]
        {
            MakeModJson("first", archive: "first.zip", sha: ShaOf(firstArchive)),
            MakeModJson("second", archive: "second.zip", sha: ShaOf(secondArchive))
        });

        var report = service.Install(manifestPath, _sourceDir, gameFolder);

        Assert.False(report.Success);
        Assert.Equal("first-v1", File.ReadAllText(firstInstalled));
        Assert.Equal("user-change", File.ReadAllText(secondInstalled));
    }

    [Fact]
    public void Install_LaterFailureRestoresEarlierUpdatedModAndJournal()
    {
        var firstArchive = Path.Combine(_sourceDir, "first.zip");
        var secondArchive = Path.Combine(_sourceDir, "second.zip");
        WriteZip(firstArchive, ("First.dll", "first-v1"));
        WriteZip(secondArchive, ("Second.dll", "second-v1"));
        var initialManifest = WriteManifest(new[]
        {
            MakeModJson("first", archive: "first.zip", sha: ShaOf(firstArchive)),
            MakeModJson("second", archive: "second.zip", sha: ShaOf(secondArchive))
        });
        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);
        var service = new DeploymentService();
        Assert.True(service.Install(initialManifest, _sourceDir, gameFolder).Success);
        var originalJournal = new JournalStore(gameFolder).Load();

        WriteZip(firstArchive, ("First.dll", "first-v2"));
        WriteZip(secondArchive, ("winhttp.dll", "rejected"));
        var updateManifest = WriteManifest(new[]
        {
            MakeModJson("first", archive: "first.zip", sha: ShaOf(firstArchive)),
            MakeModJson("second", archive: "second.zip", sha: ShaOf(secondArchive))
        });

        var report = service.Install(updateManifest, _sourceDir, gameFolder);

        Assert.False(report.Success);
        var firstInstalled = Path.Combine(gameFolder, "BepInEx", "plugins", "first", "First.dll");
        var secondInstalled = Path.Combine(gameFolder, "BepInEx", "plugins", "second", "Second.dll");
        Assert.Equal("first-v1", File.ReadAllText(firstInstalled));
        Assert.Equal("second-v1", File.ReadAllText(secondInstalled));
        Assert.Equal(
            JsonSerializer.Serialize(originalJournal.OrderBy(entry => entry.ModName)),
            JsonSerializer.Serialize(new JournalStore(gameFolder).Load().OrderBy(entry => entry.ModName)));
    }

    // --- helpers -----------------------------------------------------------

    private string WriteManifest(string[] modJsons, string fileName = "manifest.json")
    {
        var path = Path.Combine(_root, fileName);
        var json = $$"""
            {
              "manifestVersion": 1,
              "name": "Service Test",
              "game": "tcgcardshopsimulator",
              "mods": [
                {{string.Join(",\n                ", modJsons)}}
              ]
            }
            """;
        File.WriteAllText(path, json);
        return path;
    }

    private static string MakeModJson(
        string id,
        string archive = "x.zip",
        string sha = "0000000000000000000000000000000000000000000000000000000000000000",
        string[]? dependencies = null,
        string[]? conflicts = null,
        string installType = "BepInExPlugin",
        string[]? excludedArchivePaths = null)
    {
        var json = new System.Text.StringBuilder($$"""
              { "id": "{{id}}", "name": "{{id}}", "version": "1.0.0", "archive": "{{archive}}", "sha256": "{{sha}}", "installType": "{{installType}}"
            """);
        if (dependencies is { Length: > 0 })
            json.Append($", \"dependencies\": {JsonSerializer.Serialize(dependencies)}");
        if (conflicts is { Length: > 0 })
            json.Append($", \"conflicts\": {JsonSerializer.Serialize(conflicts)}");
        if (excludedArchivePaths is { Length: > 0 })
            json.Append($", \"excludedArchivePaths\": {JsonSerializer.Serialize(excludedArchivePaths)}");
        json.Append(" }");
        return json.ToString();
    }

    private static void WriteZip(string path, params (string Name, string Content)[] entries)
    {
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private static string ShaOf(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
}
