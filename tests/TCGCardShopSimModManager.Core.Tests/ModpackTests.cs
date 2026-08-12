using System.IO.Compression;
using System.Security.Cryptography;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ModpackTests : IDisposable
{
    private readonly string _root;
    private readonly LocalHttpServer _server = new();

    public ModpackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "modpack-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task IndexReader_FetchesIndex_AndResolvesUrls()
    {
        var archiveBytes = MakePayload(200);
        var sha = Sha(archiveBytes);
        var manifestJson = ManifestJson("Pack One", "ExampleMod.zip", sha);

        _server.Provider = request => request.Path switch
        {
            "/index.json" => Json(IndexJson()),
            "/p1/manifest.json" => Json(manifestJson),
            "/p1/logo.png" => new HttpResponse(200, new byte[] { 1, 2, 3 }, null),
            "/p1/ExampleMod.zip" => new HttpResponse(200, archiveBytes, null),
            _ => new HttpResponse(404, Array.Empty<byte>(), null)
        };

        var baseUrl = _server.Url("");
        var reader = new ModpackIndexReader();
        var index = await reader.FetchIndexAsync(baseUrl);

        var pack = Assert.Single(index.Packs);
        Assert.Equal("p1", pack.Id);
        Assert.Equal(_server.Url("p1/manifest.json"), reader.ManifestUrl(pack, baseUrl));
        Assert.Equal(_server.Url("p1/logo.png"), reader.LogoUrl(pack, baseUrl));

        var manifest = await reader.FetchManifestAsync(pack, baseUrl);
        Assert.Equal("Pack One", manifest.Name);
        Assert.Single(manifest.Mods);
    }

    [Fact]
    public async Task ModSource_UsesDownloadUrl_WhenPresent()
    {
        var archiveBytes = MakePayload(200);
        var mod = Ref("archive.zip", archiveBytes, downloadUrl: _server.Url("archive.zip"));

        // Fallback points at an empty folder, so success proves the DownloadUrl path was used.
        var fallback = new LocalFileSource(Path.Combine(_root, "empty"));
        _server.Provider = _ => new HttpResponse(200, archiveBytes, null);

        var result = await Download(mod, new ModpackModSource("tcgcardshopsimulator", fallback));
        Assert.True(result.Success, result.Error);
        Assert.Equal(archiveBytes, File.ReadAllBytes(Path.Combine(_root, "archive.zip")));
    }

    [Fact]
    public async Task ModSource_FallsBack_WhenNoDownloadUrlOrNexus()
    {
        var archiveBytes = MakePayload(200);
        var mod = Ref("archive.zip", archiveBytes); // no source at all
        var fallbackDir = Path.Combine(_root, "fallback");
        Directory.CreateDirectory(fallbackDir);
        File.WriteAllBytes(Path.Combine(fallbackDir, "archive.zip"), archiveBytes);

        var result = await Download(mod, new ModpackModSource("tcgcardshopsimulator", new LocalFileSource(fallbackDir)));
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task ModpackInstaller_DownloadsAndInstalls_HostedPack()
    {
        var archiveBytes = MakeZip(("ExampleMod.dll", "dll-bytes"));
        var sha = Sha(archiveBytes);
        _server.Provider = _ => new HttpResponse(200, archiveBytes, null);

        var mod = new ModEntry(
            "example-mod", "Example Mod", null, "ExampleMod.zip", sha, "BepInExPlugin",
            new List<string>(), new List<string>(), DownloadUrl: _server.Url("ExampleMod.zip"));
        var manifest = new ModListManifest(1, "Test Pack", "tcgcardshopsimulator", new List<ModEntry> { mod });

        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);

        var report = await new ModpackInstaller(gameFolder).InstallAsync(manifest);
        Assert.True(report.Success, string.Join("\n", report.Lines));

        var installed = Path.Combine(gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        Assert.True(File.Exists(installed));
        Assert.Contains(new JournalStore(gameFolder).Load(), e => e.ModName == "Example Mod");
    }

    [Fact]
    public async Task ModpackInstaller_PreflightPassesAndCleansTempCache_WhenTotalSizeDeclared()
    {
        var archiveBytes = MakeZip(("ExampleMod.dll", "dll-bytes"));
        var sha = Sha(archiveBytes);
        _server.Provider = _ => new HttpResponse(200, archiveBytes, null);

        var mod = new ModEntry(
            "example-mod", "Example Mod", null, "ExampleMod.zip", sha, "BepInExPlugin",
            new List<string>(), new List<string>(), DownloadUrl: _server.Url("ExampleMod.zip"));
        // Declaring totalSize exercises the pre-flight path; 1024 bytes is well
        // within the test machine's free space, so the install should proceed.
        var manifest = new ModListManifest(1, "Cleanup Pack", "tcgcardshopsimulator", new List<ModEntry> { mod }, TotalSize: 1024);

        var gameFolder = Path.Combine(_root, "game");
        Directory.CreateDirectory(gameFolder);

        var report = await new ModpackInstaller(gameFolder).InstallAsync(manifest);
        Assert.True(report.Success, string.Join("\n", report.Lines));

        var installed = Path.Combine(gameFolder, "BepInEx", "plugins", "Example Mod", "ExampleMod.dll");
        Assert.True(File.Exists(installed));

        // A successful install must delete the temp download cache so the
        // archives don't linger on disk.
        var cacheDir = Path.Combine(Path.GetTempPath(), "cardshopmodmanager-modpack", "cleanup pack");
        Assert.False(Directory.Exists(cacheDir), "temp modpack cache should be deleted after install");
    }

    // --- helpers -----------------------------------------------------------

    private async Task<DownloadResult> Download(ModReference mod, IModSource source) =>
        await new ModDownloader(source, new DownloadOptions { RetryBaseDelayMs = 10 })
            .DownloadAsync(mod, _root);

    private static ModReference Ref(string fileName, byte[] content, string? downloadUrl = null) =>
        new("test-mod", fileName, Sha(content), null, DownloadUrl: downloadUrl);

    private static string IndexJson() =>
        "{\"version\":1,\"packs\":[{\"id\":\"p1\",\"name\":\"Pack One\"," +
        "\"shortDescription\":\"desc\",\"logo\":\"p1/logo.png\",\"manifest\":\"p1/manifest.json\"," +
        "\"version\":\"1.0.0\",\"updated\":\"2026-08-12\",\"source\":\"https://example.com/\"}]}";

    private static string ManifestJson(string name, string archive, string sha) =>
        "{\"manifestVersion\":1,\"name\":\"" + name + "\",\"game\":\"tcgcardshopsimulator\"," +
        "\"mods\":[{\"id\":\"example-mod\",\"name\":\"Example Mod\",\"version\":null," +
        "\"archive\":\"" + archive + "\",\"sha256\":\"" + sha + "\",\"installType\":\"BepInExPlugin\"," +
        "\"dependencies\":[],\"conflicts\":[]}]}";

    private static HttpResponse Json(string body) =>
        new(200, System.Text.Encoding.UTF8.GetBytes(body), null);

    private static byte[] MakePayload(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static byte[] MakeZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), "modpack-tests-" + Guid.NewGuid().ToString("N") + ".zip");
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

        return File.ReadAllBytes(path);
    }

    private static string Sha(byte[] content)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(content)).ToLowerInvariant();
    }
}
