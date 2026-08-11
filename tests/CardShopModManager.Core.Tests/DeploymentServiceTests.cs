using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CardShopModManager.Core;

namespace CardShopModManager.Core.Tests;

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

    // --- helpers -----------------------------------------------------------

    private string WriteManifest(string[] modJsons)
    {
        var path = Path.Combine(_root, "manifest.json");
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
        string[]? conflicts = null)
    {
        var json = new System.Text.StringBuilder($$"""
              { "id": "{{id}}", "name": "{{id}}", "version": "1.0.0", "archive": "{{archive}}", "sha256": "{{sha}}", "installType": "BepInExPlugin"
            """);
        if (dependencies is { Length: > 0 })
            json.Append($", \"dependencies\": {JsonSerializer.Serialize(dependencies)}");
        if (conflicts is { Length: > 0 })
            json.Append($", \"conflicts\": {JsonSerializer.Serialize(conflicts)}");
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